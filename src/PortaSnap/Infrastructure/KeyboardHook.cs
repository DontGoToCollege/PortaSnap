using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PortaSnap.Infrastructure;

/// <summary>
/// Global hotkeys via a low-level keyboard hook. Unlike RegisterHotKey this sees Print Screen before Windows'
/// own Snipping Tool binding and can capture key combinations for the settings dialog.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    readonly Native.LowLevelKeyboardProc _proc;
    readonly List<(HotkeyBinding Binding, Action Action)> _bindings = new();
    readonly HashSet<int> _swallowUp = new();
    readonly Dispatcher _dispatcher;
    IntPtr _hook;
    Action<HotkeyBinding?>? _recorder;

    public KeyboardHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = HookProc;
    }

    public bool Suspended { get; set; }

    public bool Install()
    {
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            Log.Error($"Keyboard hook install failed (error {Marshal.GetLastWin32Error()})");
            return false;
        }
        return true;
    }

    public void Register(HotkeyBinding? binding, Action action)
    {
        if (binding != null) _bindings.Add((binding, action));
    }

    public void Clear() => _bindings.Clear();

    /// <summary>The next non-modifier key press is reported (and swallowed) instead of triggering hotkeys. Esc cancels.</summary>
    public void BeginRecord(Action<HotkeyBinding?> callback) => _recorder = callback;

    public void CancelRecord() => _recorder = null;

    static bool IsModifier(int vk) => vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or Native.VK_LWIN or Native.VK_RWIN;

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            int vk = (int)info.vkCode;
            bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool up = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            if (up && _swallowUp.Remove(vk)) return new IntPtr(1);

            // Injected keys count too, so AutoHotkey's SendInput, macro pads, Stream Decks etc. can trigger captures.
            // PortaSnap itself only injects Alt (ForceForeground), which IsModifier already skips.
            if (down && !IsModifier(vk))
            {
                bool ctrl = Native.IsKeyDown(Native.VK_CONTROL);
                bool alt = Native.IsKeyDown(Native.VK_MENU);
                bool shift = Native.IsKeyDown(Native.VK_SHIFT);
                bool win = Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN);

                if (_recorder != null)
                {
                    var cb = _recorder;
                    _recorder = null;
                    HotkeyBinding? result = vk == Native.VK_ESCAPE && !ctrl && !alt && !shift && !win
                        ? null
                        : new HotkeyBinding(vk, ctrl, alt, shift, win);
                    _swallowUp.Add(vk);
                    _dispatcher.BeginInvoke(() => cb(result));
                    return new IntPtr(1);
                }

                if (!Suspended)
                {
                    foreach (var (b, action) in _bindings)
                    {
                        if (b.Key == vk && b.Ctrl == ctrl && b.Alt == alt && b.Shift == shift && b.Win == win)
                        {
                            _swallowUp.Add(vk);
                            _dispatcher.BeginInvoke(action);
                            return new IntPtr(1);
                        }
                    }
                }
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
