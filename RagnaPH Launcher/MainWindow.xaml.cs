using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace RagnaPHPatcher
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            LoadNewsPage();
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

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
    }
}
