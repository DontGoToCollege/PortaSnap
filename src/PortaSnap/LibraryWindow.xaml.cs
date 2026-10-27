using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PortaSnap.Imaging;
using PortaSnap.Import;
using PortaSnap.Infrastructure;
using PortaSnap.Library;
using PortaSnap.Model;

namespace PortaSnap;

/// <summary>Browse, search, open, export and delete captures; import a Snagit library.</summary>
public partial class LibraryWindow : Window
{
    readonly App _app;
    List<LibraryItem> _all = new();
    Point _dragStart;
    bool _importing;

    public LibraryWindow(App app)
    {
        _app = app;
        InitializeComponent();
        _app.Library.Changed += OnLibraryChanged;
        Closed += (_, _) => _app.Library.Changed -= OnLibraryChanged;
        Refresh();
    }

    void OnLibraryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);

    void Refresh()
    {
        _all = _app.Library.Scan();
        ApplyFilter();
    }

    void ApplyFilter()
    {
        if (!IsInitialized || Items == null) return;
        IEnumerable<LibraryItem> items = _all;
        if (FilterImages.IsChecked == true) items = items.Where(i => !i.IsVideo);
        else if (FilterVideos.IsChecked == true) items = items.Where(i => i.IsVideo);
        var q = SearchBox.Text.Trim();
        if (q.Length > 0)
            items = items.Where(i => i.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || i.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || i.DateText.Contains(q, StringComparison.OrdinalIgnoreCase));
        var list = items.ToList();
        Items.ItemsSource = list;
        foreach (var i in list) _app.Library.EnsureLoaded(i);
        EmptyState.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_importing)
            StatusText.Text = $"{list.Count} of {_all.Count} items   ·   {PortablePaths.LibraryDir}";
    }

    void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();
    void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    List<LibraryItem> SelectedItems() => Items.SelectedItems.Cast<LibraryItem>().ToList();

    void OpenItem(LibraryItem item)
    {
        if (item.IsVideo) _app.ShowVideo(item.Path);
        else _app.ShowEditor().OpenLibraryItem(item);
    }

    void Items_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is LibraryItem item) OpenItem(item);
    }

    void Items_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedItems().FirstOrDefault() is { } item) { OpenItem(item); e.Handled = true; }
        else if (e.Key == Key.Delete) { Delete_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Copy_Click(sender, e); e.Handled = true; }
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItems().FirstOrDefault() is { } item) OpenItem(item);
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems();
        if (items.Count == 0) return;
        try
        {
            if (items.Count == 1 && !items[0].IsVideo)
            {
                ClipboardUtil.CopyImage(DocumentRenderer.Render(SnapFile.Load(items[0].Path)));
                StatusText.Text = "Image copied to the clipboard";
            }
            else
            {
                ClipboardUtil.CopyFiles(items.Select(FileForSharing).ToArray());
                StatusText.Text = $"{items.Count} file(s) copied to the clipboard";
            }
        }
        catch (Exception ex)
        {
            Log.Error("Library copy failed", ex);
            StatusText.Text = "Copy failed: " + ex.Message;
        }
    }

    /// <summary>Videos are shared as-is; documents are flattened to a temporary PNG.</summary>
    static string FileForSharing(LibraryItem item)
    {
        if (item.IsVideo) return item.Path;
        var doc = SnapFile.Load(item.Path);
        return ClipboardUtil.WriteTempPng(DocumentRenderer.Render(doc), Path.GetFileNameWithoutExtension(item.Path));
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItems().FirstOrDefault() is not { } item) return;
        try
        {
            if (item.IsVideo)
            {
                var dlg = new SaveFileDialog { Filter = "MP4 video|*.mp4", FileName = item.FileName };
                if (dlg.ShowDialog(this) == true) File.Copy(item.Path, dlg.FileName, overwrite: true);
            }
            else
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap|*.bmp|GIF image|*.gif|TIFF image|*.tif",
                    FileName = Path.GetFileNameWithoutExtension(item.Path),
                    InitialDirectory = Directory.Exists(_app.Settings.LastExportFolder) ? _app.Settings.LastExportFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                };
                if (dlg.ShowDialog(this) != true) return;
                ImageUtil.Save(DocumentRenderer.Render(SnapFile.Load(item.Path)), dlg.FileName, _app.Settings.JpegQuality);
                _app.Settings.LastExportFolder = Path.GetDirectoryName(dlg.FileName);
                _app.SaveSettings();
            }
            StatusText.Text = "Exported " + item.FileName;
        }
        catch (Exception ex)
        {
            Log.Error("Library export failed", ex);
            MessageBox.Show(this, "Export failed:\n\n" + ex.Message, "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItems().FirstOrDefault() is { } item) App.RevealFile(item.Path);
    }

    void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItems().FirstOrDefault() is { } item) LibraryItemActions.CopyPath(item.Path);
    }

    void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItems().FirstOrDefault() is { } item) LibraryItemActions.ShowProperties(item.Path, this);
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems();
        if (items.Count == 0) return;
        var msg = items.Count == 1 ? $"Move \"{items[0].Title}\" to the Recycle Bin?" : $"Move {items.Count} items to the Recycle Bin?";
        if (MessageBox.Show(this, msg, "PortaSnap", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _app.Library.Delete(items);
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e) => App.OpenFolder(_app.Library.Root);

    // ---------- drag out ----------

    void Items_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(this);

    void Items_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(this) - _dragStart).Length < 8) return;
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not LibraryItem) return;
        var items = SelectedItems();
        if (items.Count == 0) return;
        try
        {
            var files = new StringCollection();
            files.AddRange(items.Take(20).Select(FileForSharing).ToArray());
            var data = new DataObject();
            data.SetFileDropList(files);
            DragDrop.DoDragDrop(Items, data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Log.Error("Library drag failed", ex);
        }
    }

    // ---------- Snagit import ----------

    async void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var snagitDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Snagit");
        var dlg = new OpenFolderDialog
        {
            Title = "Choose the folder that holds your Snagit captures",
            InitialDirectory = Directory.Exists(snagitDefault) ? snagitDefault : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != true) return;
        List<string> files;
        try { files = Directory.EnumerateFiles(dlg.FolderName, "*.snagx", SearchOption.AllDirectories).ToList(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not read that folder:\n\n" + ex.Message, "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await RunImport(files);
    }

    async void ImportFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Snagit captures|*.snagx", Multiselect = true };
        var snagitDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Snagit");
        if (Directory.Exists(snagitDefault)) dlg.InitialDirectory = snagitDefault;
        if (dlg.ShowDialog(this) != true) return;
        await RunImport(dlg.FileNames.ToList());
    }

    async Task RunImport(List<string> files)
    {
        if (files.Count == 0)
        {
            MessageBox.Show(this, "No .snagx files were found there.", "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _importing = true;
        IsEnabled = false;
        ImportSummary summary;
        try
        {
            summary = await SnagxLibraryImport.RunAsync(_app, files, s => StatusText.Text = s);
        }
        finally
        {
            _importing = false;
            IsEnabled = true;
        }
        var msg = $"Imported {summary.Imported} capture(s).";
        if (summary.Skipped > 0) msg += $"\n{summary.Skipped} were already in the library.";
        if (summary.UnsupportedObjects > 0) msg += $"\n{summary.UnsupportedObjects} annotation(s) of unsupported types were left out (details in the log).";
        if (summary.Failed > 0) msg += $"\n\n{summary.Failed} could not be imported:\n" + string.Join("\n", summary.Errors.Take(8));
        MessageBox.Show(this, msg, "Snagit import", MessageBoxButton.OK, summary.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        Refresh();
    }
}
