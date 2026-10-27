using System.Globalization;
using PortaSnap.Imaging;
using PortaSnap.Import;
using PortaSnap.Model;
using PortaSnap.Video;

namespace PortaSnap.Infrastructure;

/// <summary>
/// Scriptable, window-less commands:
///   PortaSnap.exe --export &lt;input .psnap|.snagx|image&gt; &lt;output .png|.jpg|.bmp|.gif|.tif&gt;
///   PortaSnap.exe --record &lt;x,y,width,height&gt; &lt;seconds&gt; &lt;output.mp4&gt; [--fps N] [--mic] [--no-cursor]
/// Exit code 0 on success. Messages go to stdout (when redirected) and the log.
/// </summary>
public static class CommandLine
{
    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] is "--export" or "--record";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return args[0] switch
            {
                "--export" => Export(args),
                "--record" => await RecordAsync(args),
                _ => 2,
            };
        }
        catch (Exception ex)
        {
            Report("error: " + ex.Message);
            Log.Error("Command-line run failed", ex);
            return 1;
        }
    }

    static int Export(string[] args)
    {
        if (args.Length < 3) { Report("usage: --export <input> <output>"); return 2; }
        string input = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        SnapDocument doc;
        var ext = Path.GetExtension(input).ToLowerInvariant();
        int skipped = 0;
        if (ext == SnapFile.Extension) doc = SnapFile.Load(input);
        else if (ext == ".snagx")
        {
            var r = SnagxImporter.Import(input);
            doc = r.Document;
            skipped = r.SkippedObjects.Count;
        }
        else doc = new SnapDocument(ImageUtil.LoadFile(input));
        var rendered = DocumentRenderer.Render(doc);
        ImageUtil.Save(rendered, output);
        Report($"exported {rendered.PixelWidth}x{rendered.PixelHeight} with {doc.Annotations.Count} annotation(s){(skipped > 0 ? $", {skipped} unsupported skipped" : "")} -> {output}");
        return 0;
    }

    static async Task<int> RecordAsync(string[] args)
    {
        if (args.Length < 4) { Report("usage: --record <x,y,w,h> <seconds> <output.mp4> [--fps N] [--mic] [--no-cursor]"); return 2; }
        var p = args[1].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        var region = new PixelRect(p[0], p[1], p[2], p[3]);
        double seconds = double.Parse(args[2], CultureInfo.InvariantCulture);
        var output = Path.GetFullPath(args[3]);
        int fps = 30;
        int fpsIndex = Array.IndexOf(args, "--fps");
        if (fpsIndex > 0 && fpsIndex + 1 < args.Length) fps = int.Parse(args[fpsIndex + 1], CultureInfo.InvariantCulture);
        bool mic = args.Contains("--mic");
        bool cursor = !args.Contains("--no-cursor");

        var recorder = new VideoRecorder(region, fps, cursor, mic, output);
        await recorder.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        await recorder.StopAsync();
        var size = new FileInfo(output).Length;
        Report($"recorded {recorder.FramesWritten} frames ({seconds:0.#}s at {fps} fps, audio: {recorder.HasAudio}{(recorder.MicrophoneError != null ? " - " + recorder.MicrophoneError : "")}) -> {output} ({size:N0} bytes)");
        return recorder.FramesWritten > 0 && size > 0 ? 0 : 1;
    }

    static void Report(string message)
    {
        Log.Info("CLI: " + message);
        try { Console.WriteLine(message); } catch { }
    }
}
