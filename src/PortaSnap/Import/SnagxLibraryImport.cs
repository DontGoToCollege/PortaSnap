using System.Windows.Threading;
using PortaSnap.Infrastructure;

namespace PortaSnap.Import;

public sealed record ImportSummary(int Imported, int Skipped, int Failed, int UnsupportedObjects, List<string> Errors, string? LastPath);

/// <summary>Imports a batch of .snagx files into the library, skipping files that were already imported unchanged.</summary>
public static class SnagxLibraryImport
{
    public static async Task<ImportSummary> RunAsync(App app, IReadOnlyList<string> files, Action<string>? progress)
    {
        int imported = 0, skipped = 0, failed = 0, unsupported = 0;
        string? last = null;
        var errors = new List<string>();
        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            progress?.Invoke($"Importing {i + 1} of {files.Count}: {Path.GetFileName(f)}");
            await Dispatcher.Yield(DispatcherPriority.Background);

            string key;
            try { key = $"{Path.GetFullPath(f)}|{File.GetLastWriteTimeUtc(f).Ticks}"; }
            catch { key = f; }
            if (app.Settings.ImportedSnagx.Contains(key))
            {
                skipped++;
                continue;
            }
            try
            {
                var r = SnagxImporter.Import(f);
                last = app.Library.Save(r.Document);
                app.Settings.ImportedSnagx.Add(key);
                imported++;
                unsupported += r.SkippedObjects.Count;
                if (r.SkippedObjects.Count > 0)
                    Log.Info($"Imported {f} without {r.SkippedObjects.Count} object(s): {string.Join(", ", r.SkippedObjects.Distinct())}");
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{Path.GetFileName(f)}: {ex.Message}");
                Log.Error($"Snagit import failed: {f}", ex);
            }
        }
        app.SaveSettings();
        progress?.Invoke($"Import finished: {imported} imported, {skipped} already in the library, {failed} failed.");
        return new ImportSummary(imported, skipped, failed, unsupported, errors, last);
    }
}
