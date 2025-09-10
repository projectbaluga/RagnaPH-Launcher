using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace RagnaPHPatcher
{
    public partial class MainWindow : Window
    {
        private const string RemoteConfigUrl = "https://ragna.ph/patch/patchsettings.inf";

        public MainWindow()
        {
            InitializeComponent();
            LoadNewsPage();
            StartPatching();
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

        private async Task<IniFile> LoadConfig()
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    string iniContent = await wc.DownloadStringTaskAsync(new Uri(RemoteConfigUrl));
                    string[] lines = iniContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    return new IniFile(lines);
                }
            }
            catch (Exception ex)
            {
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "patchsettings.inf");
                if (File.Exists(localPath))
                {
                    MessageBox.Show("Remote config unavailable, using local patchsettings.inf.", "Config Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    string[] lines = File.ReadAllLines(localPath);
                    return new IniFile(lines);
                }

                MessageBox.Show(
                    "Missing patch settings file. The patch system may not function correctly.\n" + ex.Message,
                    "Config Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return null;
            }
        }

        private async void StartPatching()
        {
            IniFile config = await LoadConfig();
            if (config == null)
                return;

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
                using (WebClient wc = new WebClient())
                {
                    string patchListContent = await wc.DownloadStringTaskAsync(new Uri(fileUrl + patchListFile));
                    patchFiles = patchListContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to download patch list:\n" + ex.Message, "Patch Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                string relativePath = files[i].Trim();
                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                string url = baseUrl + relativePath.Replace("\\", "/");
                string finalPath = Path.Combine(baseDir, relativePath.Replace("/", "\\"));

                try
                {
                    string finalDir = Path.GetDirectoryName(finalPath);
                    if (!Directory.Exists(finalDir)) Directory.CreateDirectory(finalDir);

                    using (WebClient wc = new WebClient())
                    {
                        byte[] data = await wc.DownloadDataTaskAsync(new Uri(url));
                        File.WriteAllBytes(finalPath, data);
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
            IniFile config = await LoadConfig();
            if (config == null)
                return;

            string fileUrl = config.Get("Main", "file_url", "");
            string patchListFile = config.Get("Patch", "PatchList", "patchlist.txt");

            string[] patchFiles;
            try
            {
                using (WebClient wc = new WebClient())
                {
                    string patchListContent = await wc.DownloadStringTaskAsync(new Uri(fileUrl + patchListFile));
                    patchFiles = patchListContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to download patch list:\n" + ex.Message, "Check Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                string relativePath = files[i].Trim();
                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                string url = baseUrl + relativePath.Replace("\\", "/");
                string finalPath = Path.Combine(baseDir, relativePath.Replace("/", "\\"));

                try
                {
                    bool downloaded = false;
                    if (!File.Exists(finalPath))
                    {
                        string finalDir = Path.GetDirectoryName(finalPath);
                        if (!Directory.Exists(finalDir)) Directory.CreateDirectory(finalDir);

                        using (WebClient wc = new WebClient())
                        {
                            byte[] data = await wc.DownloadDataTaskAsync(new Uri(url));
                            File.WriteAllBytes(finalPath, data);
                        }

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
