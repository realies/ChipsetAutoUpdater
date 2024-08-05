// <copyright file="MainWindow.xaml.cs" company="nocorp">
// Copyright (c) realies. No rights reserved.
// </copyright>

namespace ChipsetAutoUpdater
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Forms;
    using Microsoft.Win32;
    using Microsoft.Win32.TaskScheduler;

    using Application = System.Windows.Application;
    using Cursors = System.Windows.Input.Cursors;
    using MessageBox = System.Windows.MessageBox;
    using SystemTask = System.Threading.Tasks.Task;
    using TaskSchedulerTask = Microsoft.Win32.TaskScheduler.Task;

    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string TaskName = "ChipsetAutoUpdaterAutoStart";
        private const int UpdateCheckIntervalHours = 6;

        private NotifyIcon notifyIcon;
        private string detectedChipsetString;
        private string installedVersionString;
        private string latestVersionReleasePageUrl;
        private string latestVersionDownloadFileUrl;
        private string latestVersionString;
        private CancellationTokenSource cancellationTokenSource;
        private HttpClient client;
        private bool isInitializing = true;
        private System.Windows.Threading.DispatcherTimer updateTimer;
        private bool autoUpdateEnabled = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            this.InitializeComponent();
            this.InitializeNotifyIcon();
            this.StateChanged += this.MainWindow_StateChanged;
            this.Loaded += this.Window_Loaded;

            string[] args = Environment.GetCommandLineArgs();
            if (args.Contains("/min"))
            {
                this.WindowState = WindowState.Minimized;
                this.Hide();
            }

            if (args.Contains("/autoupdate"))
            {
                this.autoUpdateEnabled = true;
            }

            this.Title = "CAU " + (Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false).OfType<AssemblyFileVersionAttribute>().FirstOrDefault()?.Version?.TrimEnd(".0".ToCharArray()) + " " ?? " ");

            this.client = new HttpClient();
            this.client.Timeout = TimeSpan.FromSeconds(1);
            this.client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AcmeInc/1.0)");
            this.client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            this.client.DefaultRequestHeaders.Referrer = new Uri("https://www.amd.com/");

            this.InitializeUpdateTimer();
            _ = this.RenderView();
        }

        /// <summary>
        /// Performs cleanup operations when the window is closed.
        /// Disposes of the notify icon and calls the base OnClosed method.
        /// </summary>
        /// <param name="e">A System.EventArgs that contains the event data.</param>
        protected override void OnClosed(EventArgs e)
        {
            this.notifyIcon.Dispose();
            base.OnClosed(e);
        }

        private void InitializeUpdateTimer()
        {
            this.updateTimer = new System.Windows.Threading.DispatcherTimer();
            this.updateTimer.Tick += async (sender, e) => await this.CheckForUpdates();
            this.updateTimer.Interval = TimeSpan.FromHours(UpdateCheckIntervalHours);
            this.updateTimer.Start();
        }

        private async SystemTask CheckForUpdates()
        {
            await this.RenderView();
            if (this.latestVersionString != null && this.latestVersionString != this.installedVersionString)
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
                if (this.autoUpdateEnabled)
                {
                    this.InstallDrivers_Click(this, new RoutedEventArgs());
                }
            }
        }

        private void AutoStartCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (this.isInitializing)
            {
                return;
            }

            if (this.AutoStartCheckBox.IsChecked == true)
            {
                this.CreateStartupTask();
                this.AutoUpdateCheckBox.IsEnabled = true;
            }
            else
            {
                this.RemoveStartupTask();
                this.AutoUpdateCheckBox.IsChecked = false;
                this.AutoUpdateCheckBox.IsEnabled = false;
                this.autoUpdateEnabled = false;
            }
        }

        private void AutoUpdateCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (this.isInitializing)
            {
                return;
            }

            this.autoUpdateEnabled = this.AutoUpdateCheckBox.IsChecked == true;
            this.CreateStartupTask();
        }

        private void CreateStartupTask()
        {
            using (TaskService ts = new TaskService())
            {
                TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Description = "Start ChipsetAutoUpdater at system startup";

                td.Triggers.Add(new LogonTrigger());

                string arguments = "/min";
                if (this.AutoUpdateCheckBox.IsChecked == true)
                {
                    arguments += " /autoupdate";
                }

                td.Actions.Add(new ExecAction(Assembly.GetExecutingAssembly().Location, arguments));

                td.Principal.RunLevel = TaskRunLevel.Highest;

                ts.RootFolder.RegisterTaskDefinition(TaskName, td);
            }
        }

        private void RemoveStartupTask()
        {
            using (TaskService ts = new TaskService())
            {
                ts.RootFolder.DeleteTask(TaskName, false);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            using (TaskService ts = new TaskService())
            {
                TaskSchedulerTask task = ts.GetTask(TaskName);
                this.AutoStartCheckBox.IsChecked = task != null;
                this.AutoUpdateCheckBox.IsChecked = task != null && task.Definition.Actions.OfType<ExecAction>().Any(a => a.Arguments.Contains("/autoupdate"));
            }

            this.AutoUpdateCheckBox.IsEnabled = this.AutoStartCheckBox.IsChecked == true;
            this.autoUpdateEnabled = this.AutoUpdateCheckBox.IsChecked == true;

            this.isInitializing = false;
        }

        private void InitializeNotifyIcon()
        {
            this.notifyIcon = new NotifyIcon();
            this.notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            this.notifyIcon.Visible = true;
            this.notifyIcon.Text = "Chipset Auto Updater";

            // Create context menu
            ContextMenuStrip contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open", null, this.OnOpenClick);
            contextMenu.Items.Add("Exit", null, this.OnExitClick);

            this.notifyIcon.ContextMenuStrip = contextMenu;
            this.notifyIcon.DoubleClick += this.NotifyIcon_DoubleClick;
        }

        private async void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
            else if (this.WindowState == WindowState.Normal)
            {
                await this.CheckForUpdates();
            }
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
        }

        private void OnOpenClick(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }

        private async SystemTask RenderView()
        {
            this.detectedChipsetString = this.ChipsetModelMatcher();
            this.ChipsetModelText.Text = this.detectedChipsetString ?? "Not Detected";
            this.SetInstalledVersion();

            if (this.detectedChipsetString != null)
            {
                var versionData = await this.FetchLatestVersionData(this.detectedChipsetString);
                this.latestVersionReleasePageUrl = versionData.Item1;
                this.latestVersionDownloadFileUrl = versionData.Item2;
                this.latestVersionString = versionData.Item3;
            }

            if (this.latestVersionString != null)
            {
                this.LatestVersionText.Text = this.latestVersionString;
                this.LatestVersionText.TextDecorations = TextDecorations.Underline;
                this.LatestVersionText.Cursor = Cursors.Hand;
                this.LatestVersionText.MouseLeftButtonDown += (s, e) =>
                {
                    if (this.latestVersionReleasePageUrl != null)
                    {
                        Process.Start(new ProcessStartInfo(this.latestVersionReleasePageUrl) { UseShellExecute = true });
                    }
                };
                this.InstallDriversButton.IsEnabled = true;
            }
            else
            {
                this.LatestVersionText.Text = "Error fetching";
                this.LatestVersionText.TextDecorations = null;
                this.LatestVersionText.Cursor = Cursors.Arrow;
                this.LatestVersionText.MouseLeftButtonDown -= (s, e) => { };
                this.InstallDriversButton.IsEnabled = false;
            }
        }

        private string ChipsetModelMatcher()
        {
            string registryPath = @"HARDWARE\DESCRIPTION\System\BIOS";
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath))
            {
                if (key != null)
                {
                    string product = key.GetValue("BaseBoardProduct")?.ToString().ToUpper() ?? string.Empty;
                    string pattern = @"[ABX]\d{3}E?"; // Define the regex pattern to match chipset models like A/B/X followed by three digits and optional E
                    Match match = Regex.Match(product, pattern);
                    if (match.Success)
                    {
                        return match.Value;
                    }
                }
            }

            return null;
        }

        private string GetInstalledAMDChipsetVersion()
        {
            string registryPath = @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath))
            {
                if (key != null)
                {
                    foreach (string subkeyName in key.GetSubKeyNames())
                    {
                        using (RegistryKey subkey = key.OpenSubKey(subkeyName))
                        {
                            if (subkey != null)
                            {
                                string publisher = subkey.GetValue("Publisher") as string;
                                string displayName = subkey.GetValue("DisplayName") as string;
                                if (publisher == "Advanced Micro Devices, Inc." && displayName == "AMD Chipset Software")
                                {
                                    return subkey.GetValue("DisplayVersion") as string;
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        private async Task<Tuple<string, string, string>> FetchLatestVersionData(string chipset)
        {
            try
            {
                string driversPageUrl = "https://www.amd.com/en/support/download/drivers.html";
                string driversPageContent = await this.client.GetStringAsync(driversPageUrl);
                string driversPagePattern = $@"https://[^""&]+{Regex.Escape(chipset)}\.html";
                Match driversUrlMatch = Regex.Match(driversPageContent, driversPagePattern, RegexOptions.IgnoreCase);
                if (driversUrlMatch.Success)
                {
                    string chipsetPageContent = await this.client.GetStringAsync(driversUrlMatch.Value);
                    string chipsetDownloadFileUrlPattern = $@"https://[^""]+\.exe";
                    Match chipsetDownloadFileUrlMatch = Regex.Match(chipsetPageContent, chipsetDownloadFileUrlPattern, RegexOptions.IgnoreCase);
                    string chispetReleasePageUrlPattrern = $@"/en/resources/support-articles/release-notes/.*chipset[^""]+";
                    Match chipsetRelasePageUrlMatch = Regex.Match(chipsetPageContent, chispetReleasePageUrlPattrern, RegexOptions.IgnoreCase);
                    if (chipsetDownloadFileUrlMatch.Success && chipsetRelasePageUrlMatch.Success)
                    {
                        return Tuple.Create($@"https://www.amd.com{chipsetRelasePageUrlMatch.Value}", chipsetDownloadFileUrlMatch.Value, chipsetDownloadFileUrlMatch.Value.Split('_').Last().Replace(".exe", string.Empty));
                    }
                }
            }
            catch (Exception)
            {
            }

            return Tuple.Create<string, string, string>(null, null, null);
        }

        private async void InstallDrivers_Click(object sender, RoutedEventArgs e)
        {
            string localFilePath = Path.Combine(Path.GetTempPath(), $"amd_chipset_software_{this.latestVersionString}.exe");
            try
            {
                this.InstallDriversButton.IsEnabled = false;
                this.InstallDriversButton.Visibility = Visibility.Collapsed;
                this.DownloadProgressBar.Visibility = Visibility.Visible;
                this.CancelButton.Visibility = Visibility.Visible;
                this.cancellationTokenSource = new CancellationTokenSource();

                await this.DownloadFileAsync(this.latestVersionDownloadFileUrl, localFilePath, this.cancellationTokenSource.Token);

                if (this.cancellationTokenSource.Token.IsCancellationRequested)
                {
                    return; // Do not start the process if the download was cancelled
                }

                this.CancelButton.IsEnabled = false;
                this.CancelButton.Content = "Installing...";
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = localFilePath,
                    Arguments = "-INSTALL",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                };
                Process process = Process.Start(startInfo);
                await SystemTask.Run(() => this.MonitorProcess(process));
                await SystemTask.Run(() => process.WaitForExit());
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Error downloading or installing drivers: {ex.Message}");
            }
            finally
            {
                this.InstallDriversButton.IsEnabled = true;
                this.InstallDriversButton.Visibility = Visibility.Visible;
                this.DownloadProgressBar.Value = 0;
                this.CancelButton.Visibility = Visibility.Collapsed;
                this.CancelButton.Content = "Cancel Download";
                this.CleanUpFile(localFilePath);
            }
        }

        private async SystemTask DownloadFileAsync(string requestUri, string destinationFilePath, CancellationToken cancellationToken)
        {
            try
            {
                using (var response = await this.client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    _ = response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var canReportProgress = totalBytes != -1;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var totalRead = 0L;
                        var buffer = new byte[8192];
                        var isMoreToRead = true;
                        do
                        {
                            var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                            if (read == 0)
                            {
                                isMoreToRead = false; // Download completed
                            }
                            else
                            {
                                await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                                totalRead += read;
                                if (canReportProgress)
                                {
                                    var progress = (int)((totalRead * 100) / totalBytes);
                                    this.DownloadProgressBar.Value = progress;
                                }
                            }
                        }
                        while (isMoreToRead);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                if (File.Exists(destinationFilePath))
                {
                    try
                    {
                        File.Delete(destinationFilePath);
                    }
                    catch (Exception ex)
                    {
                        _ = MessageBox.Show($"Error deleting file: {ex.Message}");
                    }
                }
            }
            catch (HttpRequestException httpRequestEx)
            {
                _ = MessageBox.Show($"HTTP request error: {httpRequestEx.Message}");
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Error during download: {ex.Message}");
                this.CleanUpFile(destinationFilePath);
            }
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            this.cancellationTokenSource?.Cancel();
        }

        private async SystemTask MonitorProcess(Process process)
        {
            while (!process.HasExited)
            {
                await this.Dispatcher.InvokeAsync(() => this.SetInstalledVersion());
                await SystemTask.Delay(1000);
            }
        }

        private void SetInstalledVersion()
        {
            this.installedVersionString = this.GetInstalledAMDChipsetVersion();
            this.InstalledVersionText.Text = this.installedVersionString ?? "Not Installed";
        }

        private void CleanUpFile(string destinationFilePath)
        {
            if (File.Exists(destinationFilePath))
            {
                try
                {
                    File.Delete(destinationFilePath);
                }
                catch (Exception deleteEx)
                {
                    _ = MessageBox.Show($"Error deleting file: {deleteEx.Message}");
                }
            }
        }
    }
}
