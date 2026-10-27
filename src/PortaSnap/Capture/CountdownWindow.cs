using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PortaSnap.Infrastructure;

namespace PortaSnap.Capture;

/// <summary>Big translucent countdown bubble; excluded from screen capture so it never appears in a shot.</summary>
public sealed class CountdownWindow : Window
{
    readonly TextBlock _number;

    CountdownWindow(string caption)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;

        _number = new TextBlock
        {
            FontSize = 64,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        };
        var grid = new Grid();
        grid.Children.Add(_number);
        grid.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 24),
        });
        Content = new Border
        {
            Width = 150,
            Height = 150,
            CornerRadius = new CornerRadius(75),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x11, 0x18, 0x27)),
            Child = grid,
        };
        SourceInitialized += (_, _) =>
        {
            var h = WindowPlacement.Handle(this);
            Native.ExcludeFromCapture(h);
            Native.MakeClickThrough(h);
        };
    }

    public static async Task RunAsync(int seconds, string caption = "Capturing in")
    {
        var w = new CountdownWindow(caption);
        w.Show();
        Native.GetCursorPos(out var p);
        WindowPlacement.CenterOn(w, Monitors.WorkAreaFromPoint(p.X, p.Y));
        for (int i = seconds; i > 0; i--)
        {
            w._number.Text = i.ToString();
            await Task.Delay(1000);
        }
        w.Close();
        await Task.Delay(60);
    }
}
