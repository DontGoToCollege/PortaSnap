using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media.Imaging;
using PortaSnap.Capture;
using PortaSnap.Infrastructure;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT;

namespace PortaSnap.Video;

/// <summary>
/// Records a screen region to H.264/AAC MP4 using the encoders that ship with Windows (Media Foundation via
/// MediaTranscoder + MediaStreamSource). Frames come from GDI capture; optional microphone audio from AudioGraph.
/// No external codecs or downloads are needed, so this works fully offline.
/// </summary>
public sealed class VideoRecorder
{
    // MF_MT_DEFAULT_STRIDE: a positive stride tells Media Foundation our BGRA rows are top-down.
    static readonly Guid MfMtDefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");

    readonly PixelRect _region;
    readonly int _fps;
    readonly bool _cursor;
    readonly bool _microphone;
    readonly string _path;
    readonly int _width;
    readonly int _height;
    readonly BlockingCollection<MediaStreamSample> _video = new(boundedCapacity: 8);
    readonly ConcurrentBag<byte[]> _pool = new();
    readonly Stopwatch _clock = new();

    BlockingCollection<MediaStreamSample>? _audio;
    MediaStreamSource? _source;
    IRandomAccessStream? _stream;
    Task? _transcode;
    Thread? _thread;
    AudioGraph? _graph;
    AudioDeviceInputNode? _audioIn;
    AudioFrameOutputNode? _audioOut;
    uint _audioRate;
    uint _bytesPerAudioFrame;
    long _audioFrames;
    volatile bool _stopping;
    volatile bool _paused;
    TimeSpan _pausedTotal;
    TimeSpan _pauseStarted;

    public VideoRecorder(PixelRect region, int fps, bool cursor, bool microphone, string path)
    {
        // H.264 needs even dimensions.
        _width = Math.Max(2, region.Width & ~1);
        _height = Math.Max(2, region.Height & ~1);
        _region = new PixelRect(region.X, region.Y, _width, _height);
        _fps = Math.Clamp(fps, 5, 60);
        _cursor = cursor;
        _microphone = microphone;
        _path = path;
    }

    public BitmapSource? FirstFrame { get; private set; }
    public string? MicrophoneError { get; private set; }
    public bool HasAudio => _audio != null;
    public string OutputPath => _path;
    public bool IsPaused => _paused;
    public long FramesWritten { get; private set; }

    public TimeSpan Elapsed => _clock.Elapsed - _pausedTotal - (_paused ? _clock.Elapsed - _pauseStarted : TimeSpan.Zero);

    public async Task StartAsync()
    {
        var vprops = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_width, (uint)_height);
        vprops.FrameRate.Numerator = (uint)_fps;
        vprops.FrameRate.Denominator = 1;
        vprops.PixelAspectRatio.Numerator = 1;
        vprops.PixelAspectRatio.Denominator = 1;
        vprops.Properties[MfMtDefaultStride] = (uint)(_width * 4);
        var vdesc = new VideoStreamDescriptor(vprops) { Name = "video" };

        AudioStreamDescriptor? adesc = null;
        if (_microphone)
        {
            try
            {
                await InitAudioAsync();
                adesc = new AudioStreamDescriptor(_graph!.EncodingProperties) { Name = "audio" };
            }
            catch (Exception ex)
            {
                MicrophoneError = ex.Message;
                Log.Error("Microphone unavailable; recording without audio", ex);
                DisposeAudio();
            }
        }

        _source = adesc == null ? new MediaStreamSource(vdesc) : new MediaStreamSource(vdesc, adesc);
        _source.BufferTime = TimeSpan.Zero;
        _source.CanSeek = false;
        _source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        _source.SampleRequested += OnSampleRequested;

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)_width;
        profile.Video.Height = (uint)_height;
        profile.Video.FrameRate.Numerator = (uint)_fps;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Video.Bitrate = (uint)Math.Clamp((long)_width * _height * _fps / 7, 1_500_000, 40_000_000);
        if (adesc == null)
        {
            profile.Audio = null;
        }
        else
        {
            uint channels = Math.Min(2u, _graph!.EncodingProperties.ChannelCount);
            uint rate = _graph.EncodingProperties.SampleRate is 44100 or 48000 ? _graph.EncodingProperties.SampleRate : 48000;
            profile.Audio = AudioEncodingProperties.CreateAac(rate, channels, 160000);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _stream = await FileRandomAccessStream.OpenAsync(_path, FileAccessMode.ReadWrite, StorageOpenOptions.None, FileOpenDisposition.CreateAlways);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true, AlwaysReencode = true };
        var prep = await transcoder.PrepareMediaStreamSourceTranscodeAsync(_source, _stream, profile);
        if (!prep.CanTranscode)
            throw new InvalidOperationException($"The Windows video encoder is not available ({prep.FailureReason}).");

        _clock.Start();
        _graph?.Start();
        Native.timeBeginPeriod(1);
        _thread = new Thread(CaptureLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "PortaSnap video capture" };
        _thread.Start();
        _transcode = prep.TranscodeAsync().AsTask();
    }

    public void Pause()
    {
        if (_paused) return;
        _pauseStarted = _clock.Elapsed;
        _paused = true;
    }

    public void Resume()
    {
        if (!_paused) return;
        _pausedTotal += _clock.Elapsed - _pauseStarted;
        _paused = false;
    }

    /// <summary>Stops capture, flushes the encoder and closes the file.</summary>
    public async Task StopAsync()
    {
        if (_paused) Resume();
        _stopping = true;
        _thread?.Join(3000);
        if (!_video.IsAddingCompleted) _video.CompleteAdding();
        try { _graph?.Stop(); } catch { }
        if (_audio != null && !_audio.IsAddingCompleted) _audio.CompleteAdding();
        try
        {
            if (_transcode != null) await _transcode;
        }
        finally
        {
            Native.timeEndPeriod(1);
            DisposeAudio();
            _stream?.Dispose();
            _stream = null;
        }
    }

    TimeSpan Timestamp() => _clock.Elapsed - _pausedTotal;

    void CaptureLoop()
    {
        try
        {
            using var grabber = new GdiFrameGrabber(_width, _height);
            var interval = TimeSpan.FromSeconds(1.0 / _fps);
            var next = TimeSpan.Zero;
            while (!_stopping)
            {
                if (_paused)
                {
                    Thread.Sleep(10);
                    next = Timestamp();
                    continue;
                }
                var ts = Timestamp();
                if (ts < next)
                {
                    var wait = next - ts;
                    Thread.Sleep(wait.TotalMilliseconds > 2 ? (int)wait.TotalMilliseconds - 1 : 0);
                    continue;
                }

                grabber.Grab(_region.X, _region.Y, _cursor, captureLayered: false);
                FirstFrame ??= grabber.ToBitmapSource();

                if (!_pool.TryTake(out var bytes) || bytes.Length != grabber.ByteCount) bytes = new byte[grabber.ByteCount];
                grabber.CopyTo(bytes);
                var sample = MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), ts);
                sample.Duration = interval;
                var captured = bytes;
                sample.Processed += (_, _) => { if (_pool.Count < 12) _pool.Add(captured); };
                if (!_video.TryAdd(sample, 50))
                {
                    // Encoder is behind; drop this frame rather than stall the capture clock.
                    if (_pool.Count < 12) _pool.Add(captured);
                }
                else
                {
                    FramesWritten++;
                }

                next += interval;
                if (Timestamp() - next > TimeSpan.FromMilliseconds(250)) next = Timestamp(); // fell far behind: resync
            }
        }
        catch (Exception ex)
        {
            Log.Error("Video capture loop failed", ex);
        }
        finally
        {
            if (!_video.IsAddingCompleted) _video.CompleteAdding();
        }
    }

    void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var request = args.Request;
        var queue = request.StreamDescriptor.Name == "audio" ? _audio : _video;
        if (queue == null)
        {
            request.Sample = null;
            return;
        }
        if (queue.TryTake(out var ready))
        {
            request.Sample = ready;
            return;
        }
        if (queue.IsCompleted)
        {
            request.Sample = null;
            return;
        }
        var deferral = request.GetDeferral();
        Task.Run(() =>
        {
            try
            {
                MediaStreamSample? sample = null;
                try { sample = queue.Take(); } catch (InvalidOperationException) { }
                request.Sample = sample;
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    async Task InitAudioAsync()
    {
        var settings = new AudioGraphSettings(AudioRenderCategory.Media);
        var created = await AudioGraph.CreateAsync(settings);
        if (created.Status != AudioGraphCreationStatus.Success)
            throw new InvalidOperationException($"Audio graph: {created.Status}");
        _graph = created.Graph;

        var input = await _graph.CreateDeviceInputNodeAsync(Windows.Media.Capture.MediaCategory.Media);
        if (input.Status != AudioDeviceNodeCreationStatus.Success)
            throw new InvalidOperationException(input.Status == AudioDeviceNodeCreationStatus.AccessDenied
                ? "Microphone access is blocked (Windows Settings › Privacy › Microphone › Let desktop apps access your microphone)."
                : $"Microphone: {input.Status}");
        _audioIn = input.DeviceInputNode;
        _audioOut = _graph.CreateFrameOutputNode();
        _audioIn.AddOutgoingConnection(_audioOut);

        var p = _graph.EncodingProperties;
        _audioRate = p.SampleRate;
        _bytesPerAudioFrame = Math.Max(1, p.BitsPerSample / 8 * p.ChannelCount);
        _audio = new BlockingCollection<MediaStreamSample>(boundedCapacity: 400);
        _graph.QuantumStarted += OnAudioQuantum;
    }

    unsafe void OnAudioQuantum(AudioGraph sender, object args)
    {
        if (_audioOut == null || _audio == null) return;
        using var frame = _audioOut.GetFrame();
        if (_stopping || _paused) return;
        using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
        uint length = buffer.Length;
        if (length == 0) return;
        using var reference = buffer.CreateReference();
        byte* data = GetBufferPointer(reference, out uint capacity);
        if (data == null) return;
        int count = (int)Math.Min(length, capacity);
        var bytes = new byte[count];
        Marshal.Copy((IntPtr)data, bytes, 0, count);

        long frames = count / _bytesPerAudioFrame;
        var ts = TimeSpan.FromSeconds((double)_audioFrames / _audioRate);
        _audioFrames += frames;
        var sample = MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), ts);
        sample.Duration = TimeSpan.FromSeconds((double)frames / _audioRate);
        try { _audio.TryAdd(sample); } catch (InvalidOperationException) { }
    }

    /// <summary>IMemoryBufferByteAccess::GetBuffer via a raw COM call (avoids needing a projected interop interface).</summary>
    static unsafe byte* GetBufferPointer(IMemoryBufferReference reference, out uint capacity)
    {
        capacity = 0;
        var objRef = ((IWinRTObject)reference).NativeObject;
        var iid = new Guid("5b0d3235-4dba-4d44-865e-8f1d0e4fd04d");
        if (Marshal.QueryInterface(objRef.ThisPtr, ref iid, out IntPtr access) != 0 || access == IntPtr.Zero) return null;
        try
        {
            byte* value;
            uint cap;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, int>)(*(*(void***)access + 3));
            int hr = fn(access, &value, &cap);
            if (hr != 0) return null;
            capacity = cap;
            return value;
        }
        finally
        {
            Marshal.Release(access);
        }
    }

    void DisposeAudio()
    {
        try
        {
            if (_graph != null) _graph.QuantumStarted -= OnAudioQuantum;
            _audioIn?.Dispose();
            _audioOut?.Dispose();
            _graph?.Dispose();
        }
        catch { }
        _audioIn = null;
        _audioOut = null;
        _graph = null;
    }
}
