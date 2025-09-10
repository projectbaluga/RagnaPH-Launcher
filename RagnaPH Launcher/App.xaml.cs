using System;
using System.IO;
using System.Net;
using System.Windows;
using RagnaPHPatcher;

namespace RagnaPH_Launcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Allow applying patches from the command line without showing the UI.
            if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--apply-patch", StringComparison.OrdinalIgnoreCase))
            {
                var source = e.Args[1];
                var grfPath = e.Args.Length >= 3 ? e.Args[2] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.grf");
                var patchPath = source;
                bool downloaded = false;

                try
                {
                    if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        patchPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(uri.LocalPath));
                        using (var client = new WebClient())
                            client.DownloadFile(uri, patchPath);
                        downloaded = true;
                    }

                    if (!string.Equals(Path.GetExtension(patchPath), ".thor", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Patch file is not a .thor archive.");

                    var progress = new Progress<ThorPatcher.PatchProgress>(p =>
                        Console.WriteLine($"[{p.Index}/{p.Count}] {p.Path}"));

                    ThorPatcher.ApplyPatch(patchPath, grfPath, progress);
                    Console.WriteLine("Patch applied successfully.");
                    Environment.ExitCode = 0;
                    if (downloaded)
                    {
                        try { File.Delete(patchPath); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed applying thor patch: {source}\n{ex.Message}");
                    Environment.ExitCode = 1;
                }

                Shutdown();
                return;
            }

            base.OnStartup(e);
        }
    }
}
