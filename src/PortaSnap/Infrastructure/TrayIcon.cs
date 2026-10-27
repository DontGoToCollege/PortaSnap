using WinForms = System.Windows.Forms;

namespace PortaSnap.Infrastructure;

/// <summary>Notification-area icon with the capture commands; keeps PortaSnap reachable while its windows are hidden.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly WinForms.NotifyIcon _icon;

    public TrayIcon(App app)
    {
        _icon = new WinForms.NotifyIcon
        {
            Text = "PortaSnap",
            Icon = LoadIcon(),
            Visible = true,
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Capture region or window", null, (_, _) => app.Run(() => app.Capture.CaptureImageAsync(ImageCaptureType.Region)));
        menu.Items.Add("Capture full screen", null, (_, _) => app.Run(() => app.Capture.CaptureImageAsync(ImageCaptureType.FullScreen)));
        menu.Items.Add("Scrolling capture", null, (_, _) => app.Run(() => app.Capture.CaptureImageAsync(ImageCaptureType.Scrolling)));
        menu.Items.Add("Record video", null, (_, _) => app.Run(() => app.Capture.ToggleVideoAsync()));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Capture panel", null, (_, _) => app.ShowCapturePanel());
        menu.Items.Add("Editor", null, (_, _) => app.ShowEditor());
        menu.Items.Add("Library", null, (_, _) => app.ShowLibrary());
        menu.Items.Add("Settings", null, (_, _) => app.ShowSettings());
        menu.Items.Add("Open data folder", null, (_, _) => App.OpenFolder(PortablePaths.DataDir));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit PortaSnap", null, (_, _) => app.ExitApp());
        _icon.ContextMenuStrip = menu;

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) app.ShowCapturePanel();
        };
    }

    static System.Drawing.Icon LoadIcon()
    {
        var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PortaSnap.ico"));
        using var s = res!.Stream;
        return new System.Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
    }

    public void ShowBalloon(string title, string text)
    {
        try { _icon.ShowBalloonTip(3000, title, text, WinForms.ToolTipIcon.None); } catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
