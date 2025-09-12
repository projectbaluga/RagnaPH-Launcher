using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace RagnaPHPatcher
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient Http = CreateHttpClient();

        public MainWindow()
        {
            InitializeComponent();
            LoadNewsPage();
            StartPatching();
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = ValidateCertificate;
            return new HttpClient(handler);
        }

        private static bool ValidateCertificate(HttpRequestMessage request, X509Certificate2? cert, X509Chain? chain, SslPolicyErrors errors)
        {
            if (request.RequestUri?.Host.Equals("ragna.ph", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Accept the certificate for the patch host even if it is self-signed
                return true;
            }

            return errors == SslPolicyErrors.None;
        }

        private static string CombineUrl(string baseUrl, string relativePath)
        {
            if (!baseUrl.EndsWith("/"))
                baseUrl += "/";

            relativePath = relativePath.Replace("\\", "/").TrimStart('/');
            var segments = relativePath.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(Uri.EscapeDataString);
            return baseUrl + string.Join("/", segments);
        }

        private void LoadNewsPage()
        {
            NewsWebBrowser.Navigate("https://ragna.ph/?module=news");
            NewsWebBrowser.LoadCompleted += NewsWebBrowser_LoadCompleted;
        }

        private void NewsWebBrowser_LoadCompleted(object sender, NavigationEventArgs e)
        {
            string script = @"
                (function(){
                    var navbar = document.querySelector('.navbar');
                    if (navbar) navbar.style.display = 'none';

                    var footer = document.querySelector('#footer') || document.querySelector('.footer');
                    if (footer) footer.style.display = 'none';

                    var body = document.querySelector('body');
                    if (body) {
                        body.style.marginTop = '-140px';
                        body.style.marginBottom = '-10px';
                    }
                })();";

            try
            {
                NewsWebBrowser.InvokeScript("eval", new object[] { script });
            }
            catch (Exception ex)
            {
                MessageBox.Show("JS Injection Failed:\n" + ex.Message, "Browser", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void StartPatching()
        {
            IniFile config;

            try
            {
                string iniContent = await Http.GetStringAsync("https://ragna.ph/patcher/config.ini");
                string[] lines = iniContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                config = new IniFile(lines);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load remote config:\n" + ex.Message, "Config Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool allow = config.Get("Main", "allow", "false").ToLower() == "true";
            bool forceStart = config.Get("Main", "Force_Start", "false").ToLower() == "true";
            string policyMsg = config.Get("Main", "policy_msg", "Server under maintenance.");
            string fileUrl = config.Get("Main", "file_url", "");
            string patchListFile = config.Get("Patch", "PatchList", "patchlist.txt");

            if (!allow)
            {
                MessageBox.Show(policyMsg, "Patch Policy", MessageBoxButton.OK, MessageBoxImage.Warning);
                if (!forceStart)
                {
                    this.Close();
                    return;
                }
            }

            string[] patchFiles;
            try
            {
                string? patchListContent = null;
                string patchListUrl = string.Empty;
                foreach (var candidate in new[] { patchListFile, "plist.txt", "patchlist.txt" })
                {
                    patchListUrl = CombineUrl(fileUrl, candidate);
                    try
                    {
                        patchListContent = await Http.GetStringAsync(patchListUrl);
                        break;
                    }
                    catch
                    {
                        // try next candidate
                    }
                }

                if (patchListContent is null)
                    throw new HttpRequestException("Patch list not found.");

                patchFiles = patchListContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to download patch list from " + CombineUrl(fileUrl, patchListFile) + ":\n" + ex.Message,
                    "Patch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await DownloadPatchFiles(fileUrl, patchFiles);
        }

        private async Task DownloadPatchFiles(string baseUrl, string[] files)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            int total = files.Length;

            for (int i = 0; i < total; i++)
            {
                string relativePath = files[i].Split(new[] { '|', ',' }, 2)[0].Trim();
                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                string url = CombineUrl(baseUrl, relativePath.Replace("\\", "/"));
                string finalPath = Path.Combine(baseDir, relativePath.Replace("/", "\\"));

                try
                {
                    string finalDir = Path.GetDirectoryName(finalPath);
                    if (!Directory.Exists(finalDir)) Directory.CreateDirectory(finalDir);

                    byte[] data = await Http.GetByteArrayAsync(url);
                    File.WriteAllBytes(finalPath, data);

                    if (Path.GetExtension(finalPath).Equals(".thor", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string grfPath = Path.Combine(baseDir, "data.grf");
                            ThorPatcher.ApplyPatch(finalPath, grfPath);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Failed applying thor patch: " + relativePath + "\n" + ex.Message,
                                "Patch Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed patching: " + relativePath + "\n" + ex.Message, "Patch Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                int progress = (int)(((i + 1) / (double)total) * 100);
                PatcherProgressBar.Value = progress;
                ProgressText.Text = "Patching: " + relativePath + " (" + progress + "%)";
            }

            ProgressText.Text = "Patching complete.";
        }

        private void LaunchGameButton_Click(object sender, RoutedEventArgs e)
        {
            string gamePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RagnaPH.exe");

            if (!File.Exists(gamePath))
            {
                MessageBox.Show("Game not found.", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string safePlayPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SafePlay.exe");

                if (File.Exists(safePlayPath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = safePlayPath,
                        Arguments = "\"" + gamePath + "\"",
                        WorkingDirectory = Path.GetDirectoryName(safePlayPath)
                    };

                    Process.Start(startInfo);
                }
                else
                {
                    Process.Start(gamePath);
                }

                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Launch failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void CheckFileButton_Click(object sender, RoutedEventArgs e)
        {
            IniFile config;

            try
            {
                string iniContent = await Http.GetStringAsync("https://ragna.ph/patcher/config.ini");
                string[] lines = iniContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                config = new IniFile(lines);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load remote config:\n" + ex.Message, "Config Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string fileUrl = config.Get("Main", "file_url", "");
            string patchListFile = config.Get("Patch", "PatchList", "patchlist.txt");

            string[] patchFiles;
            try
            {
                string patchListUrl = CombineUrl(fileUrl, patchListFile);
                string patchListContent = await Http.GetStringAsync(patchListUrl);
                patchFiles = patchListContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to download patch list from " + CombineUrl(fileUrl, patchListFile) + ":\n" + ex.Message,
                    "Check Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await CheckPatchFiles(fileUrl, patchFiles);
        }

        private async Task CheckPatchFiles(string baseUrl, string[] files)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            int total = files.Length;

            for (int i = 0; i < total; i++)
            {
                string relativePath = files[i].Split(new[] { '|', ',' }, 2)[0].Trim();
                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                string url = CombineUrl(baseUrl, relativePath.Replace("\\", "/"));
                string finalPath = Path.Combine(baseDir, relativePath.Replace("/", "\\"));

                try
                {
                    bool downloaded = false;
                    if (!File.Exists(finalPath))
                    {
                        string finalDir = Path.GetDirectoryName(finalPath);
                        if (!Directory.Exists(finalDir)) Directory.CreateDirectory(finalDir);

                        byte[] data = await Http.GetByteArrayAsync(url);
                        File.WriteAllBytes(finalPath, data);

                        downloaded = true;
                    }

                    if (Path.GetExtension(finalPath).Equals(".thor", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string grfPath = Path.Combine(baseDir, "data.grf");
                            ThorPatcher.ApplyPatch(finalPath, grfPath);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Failed applying thor patch: " + relativePath + "\n" + ex.Message,
                                "Check Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            continue;
                        }
                    }

                    if (downloaded)
                    {
                        // Optionally report file download
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed checking: " + relativePath + "\n" + ex.Message, "Check Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                int progress = (int)(((i + 1) / (double)total) * 100);
                PatcherProgressBar.Value = progress;
                ProgressText.Text = "Checking: " + relativePath + " (" + progress + "%)";
            }

            ProgressText.Text = "File check complete.";
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
    }
}
