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
            if (e.Args.Length >= 3 && string.Equals(e.Args[0], "--apply-patch", StringComparison.OrdinalIgnoreCase))
            {
                var source = e.Args[1];
                var grfPath = e.Args[2];
                var patchPath = source;

                try
                {
                    if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        patchPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(uri.LocalPath));
                        using (var client = new WebClient())
                            client.DownloadFile(uri, patchPath);
                    }

                    ThorPatcher.ApplyPatch(patchPath, grfPath);
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed applying thor patch: {source}\n{ex.Message}");
                    Environment.ExitCode = 1;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(patchPath))
                            File.Delete(patchPath);
                    }
                    catch { }
                }

                Shutdown();
                return;
            }

            base.OnStartup(e);
        }
    }
}
