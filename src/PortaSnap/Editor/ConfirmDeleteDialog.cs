using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PortaSnap.Editor;

/// <summary>
/// "Move to the Recycle Bin?" prompt with a "Don't ask me again" box. Once ticked, deletes happen without asking
/// until the option is switched back on in Settings.
/// </summary>
public sealed class ConfirmDeleteDialog : Window
{
    readonly CheckBox _dontAsk;

    ConfirmDeleteDialog(string message)
    {
        Title = "Delete capture";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var icon = new TextBlock
        {
            Text = "",
            FontSize = 26,
            Margin = new Thickness(0, 2, 14, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        var text = new StackPanel { MaxWidth = 380 };
        text.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        var hint = new TextBlock { Text = "You can restore it from the Recycle Bin.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        text.Children.Add(hint);
        var top = new DockPanel { Margin = new Thickness(20, 18, 20, 12) };
        top.Children.Add(icon);
        top.Children.Add(text);

        _dontAsk = new CheckBox { Content = "Don't ask me again", VerticalAlignment = VerticalAlignment.Center };
        var yes = new Button { Content = "Yes", Style = (Style)Application.Current.FindResource("PrimaryButton"), MinWidth = 80, IsDefault = true, Margin = new Thickness(16, 0, 8, 0) };
        yes.Click += (_, _) => DialogResult = true;
        var no = new Button { Content = "No", Style = (Style)Application.Current.FindResource("SecondaryButton"), MinWidth = 80, IsCancel = true };
        var bottom = new DockPanel { Margin = new Thickness(20, 4, 20, 16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_dontAsk);

        var root = new StackPanel { MinWidth = 380 };
        root.Children.Add(top);
        root.Children.Add(bottom);
        Content = root;
        Loaded += (_, _) => yes.Focus();
    }

    /// <summary>Asks unless the user turned confirmations off. Returns true when the delete should go ahead.</summary>
    public static bool Confirm(Window owner, App app, string message)
    {
        if (!app.Settings.ConfirmDelete) return true;
        var dlg = new ConfirmDeleteDialog(message) { Owner = owner };
        bool ok = dlg.ShowDialog() == true;
        if (dlg._dontAsk.IsChecked == true)
        {
            app.Settings.ConfirmDelete = false;
            app.SaveSettings();
        }
        return ok;
    }
}
