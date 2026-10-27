using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PortaSnap.Editor;

/// <summary>Base for the small modal dialogs (built in code to keep them compact).</summary>
public abstract class SmallDialog : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(18, 14, 18, 8) };

    protected SmallDialog(string title)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("WindowBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var ok = new Button { Content = "OK", Style = (Style)Application.Current.FindResource("PrimaryButton"), MinWidth = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            if (Validate()) DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.FindResource("SecondaryButton"), MinWidth = 80, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 4, 18, 16) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { MinWidth = 300 };
        root.Children.Add(Body);
        root.Children.Add(buttons);
        Content = root;
    }

    protected abstract bool Validate();

    protected static TextBox NumberBox(int value) => new() { Text = value.ToString(CultureInfo.InvariantCulture), Width = 90, Padding = new Thickness(4, 3, 4, 3) };

    protected static int? ReadInt(TextBox box, int min, int max)
        => int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max ? v : null;

    protected static Grid Row(string label, UIElement control, string? suffix = null)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(l);
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        if (suffix != null)
        {
            var s = new TextBlock { Text = suffix, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Foreground = Brushes.Gray };
            Grid.SetColumn(s, 2);
            g.Children.Add(s);
        }
        return g;
    }
}

public sealed class ResizeDialog : SmallDialog
{
    const int MaxSide = 30000;
    readonly int _origW;
    readonly int _origH;
    readonly TextBox _w;
    readonly TextBox _h;
    readonly CheckBox _keep;
    bool _syncing;

    public ResizeDialog(int width, int height) : base("Resize image")
    {
        _origW = width;
        _origH = height;
        _w = NumberBox(width);
        _h = NumberBox(height);
        _keep = new CheckBox { Content = "Keep proportions", IsChecked = true, Margin = new Thickness(0, 6, 0, 6) };
        Body.Children.Add(new TextBlock { Text = $"Current size: {width} × {height} px", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8) });
        Body.Children.Add(Row("Width", _w, "px"));
        Body.Children.Add(Row("Height", _h, "px"));
        Body.Children.Add(_keep);
        var presets = new WrapPanel();
        foreach (var pct in new[] { 25, 50, 75, 150, 200 })
        {
            var b = new Button { Content = $"{pct}%", Style = (Style)Application.Current.FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 3, 8, 3) };
            b.Click += (_, _) =>
            {
                _syncing = true;
                _w.Text = Math.Max(1, (int)Math.Round(_origW * pct / 100.0)).ToString(CultureInfo.InvariantCulture);
                _h.Text = Math.Max(1, (int)Math.Round(_origH * pct / 100.0)).ToString(CultureInfo.InvariantCulture);
                _syncing = false;
            };
            presets.Children.Add(b);
        }
        Body.Children.Add(presets);

        _w.TextChanged += (_, _) => Sync(fromWidth: true);
        _h.TextChanged += (_, _) => Sync(fromWidth: false);
        Loaded += (_, _) => { _w.Focus(); _w.SelectAll(); };
    }

    public int ResultWidth { get; private set; }
    public int ResultHeight { get; private set; }

    void Sync(bool fromWidth)
    {
        if (_syncing || _keep.IsChecked != true) return;
        _syncing = true;
        if (fromWidth && ReadInt(_w, 1, MaxSide) is int w) _h.Text = Math.Max(1, (int)Math.Round(w * (double)_origH / _origW)).ToString(CultureInfo.InvariantCulture);
        if (!fromWidth && ReadInt(_h, 1, MaxSide) is int h) _w.Text = Math.Max(1, (int)Math.Round(h * (double)_origW / _origH)).ToString(CultureInfo.InvariantCulture);
        _syncing = false;
    }

    protected override bool Validate()
    {
        if (ReadInt(_w, 1, MaxSide) is not int w || ReadInt(_h, 1, MaxSide) is not int h)
        {
            MessageBox.Show(this, $"Enter a width and height between 1 and {MaxSide}.", "Resize", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        ResultWidth = w;
        ResultHeight = h;
        return true;
    }
}

public sealed class CanvasDialog : SmallDialog
{
    readonly TextBox _l = NumberBox(20);
    readonly TextBox _t = NumberBox(20);
    readonly TextBox _r = NumberBox(20);
    readonly TextBox _b = NumberBox(20);
    readonly ComboBox _fill;

    public CanvasDialog() : base("Expand canvas")
    {
        Body.Children.Add(new TextBlock { Text = "Add space around the image:", Margin = new Thickness(0, 0, 0, 8) });
        Body.Children.Add(Row("Left", _l, "px"));
        Body.Children.Add(Row("Top", _t, "px"));
        Body.Children.Add(Row("Right", _r, "px"));
        Body.Children.Add(Row("Bottom", _b, "px"));
        _fill = new ComboBox { Width = 140 };
        foreach (var name in new[] { "Transparent", "White", "Black", "Light gray" }) _fill.Items.Add(name);
        _fill.SelectedIndex = 1;
        Body.Children.Add(Row("Fill", _fill));
    }

    public int PadLeft { get; private set; }
    public int PadTop { get; private set; }
    public int PadRight { get; private set; }
    public int PadBottom { get; private set; }

    public Color Fill => _fill.SelectedIndex switch
    {
        0 => Colors.Transparent,
        2 => Colors.Black,
        3 => Color.FromRgb(0xE5, 0xE7, 0xEB),
        _ => Colors.White,
    };

    protected override bool Validate()
    {
        if (ReadInt(_l, 0, 5000) is not int l || ReadInt(_t, 0, 5000) is not int t || ReadInt(_r, 0, 5000) is not int r || ReadInt(_b, 0, 5000) is not int b)
        {
            MessageBox.Show(this, "Enter amounts between 0 and 5000.", "Canvas", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        PadLeft = l;
        PadTop = t;
        PadRight = r;
        PadBottom = b;
        return true;
    }
}
