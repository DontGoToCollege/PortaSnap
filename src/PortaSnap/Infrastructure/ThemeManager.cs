using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace PortaSnap.Infrastructure;

/// <summary>
/// Light/dark theming. The colour palette is merged dictionary 0 of the application resources and is swapped
/// at runtime; every brush in the UI is a DynamicResource, so open windows update immediately. Title bars are
/// switched with DWM's immersive dark mode attribute. "System" follows Windows' app theme setting live.
/// </summary>
public static class ThemeManager
{
    const int DwmUseImmersiveDarkMode = 20;

    static readonly Uri LightUri = new("pack://application:,,,/Themes/Light.xaml");
    static readonly Uri DarkUri = new("pack://application:,,,/Themes/Dark.xaml");

    public static bool IsDark { get; private set; }
    public static event EventHandler? Changed;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Initialize(App app)
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => ApplyTitleBar((Window)s)));
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && app.Settings.Theme == AppTheme.System)
                app.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
        };
        Apply(app.Settings.Theme);
    }

    public static void Apply(AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark || (theme == AppTheme.System && SystemPrefersDark());
        var merged = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        if (merged.Count > 0) merged[0] = palette;
        else merged.Add(palette);

        bool changed = dark != IsDark;
        IsDark = dark;
        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        if (changed) Changed?.Invoke(null, EventArgs.Empty);
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero || w.WindowStyle == WindowStyle.None) return;
        int value = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int));
    }
}
