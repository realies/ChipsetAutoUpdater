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
    using Microsoft.Win32;

    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// </summary>
    public partial class MainWindow : Window
    {
        private string detectedChipset;
        private string installedVersion;
        private string latestVersionUrl;
        private string latestVersion;
        private CancellationTokenSource cancellationTokenSource;
        private HttpClient client = new HttpClient();

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            this.InitializeComponent();

            this.Title = "Chipset Auto Updater " + (Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false).OfType<AssemblyFileVersionAttribute>().FirstOrDefault()?.Version?.TrimEnd(".0".ToCharArray()) + " " ?? " ") + "Alpha";

            async Task InitializeAsync()
            {
                this.client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AcmeInc/1.0)");
                this.client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                this.client.DefaultRequestHeaders.Referrer = new Uri("https://www.amd.com/");
                this.client.Timeout = TimeSpan.FromSeconds(1);

                this.detectedChipset = this.ChipsetModelMatcher();
                this.ChipsetModelText.Text = this.detectedChipset ?? "Not Detected";
                this.SetInstalledVersion();

                if (this.detectedChipset != null)
                {
                    this.latestVersionUrl = await this.FetchLatestVersionUrl(this.detectedChipset);
                    this.latestVersion = this.latestVersionUrl?.Split('_').Last().Replace(".exe", string.Empty);
                    if (this.latestVersion != null)
                    {
                        this.InstallDriversButton.IsEnabled = true;
                    }
                }

                this.LatestVersionText.Text = this.latestVersion ?? "Error fetching";
            }

            _ = InitializeAsync();
        }

        /// <summary>
        /// Attempt to match AM4 or AM5 model name via a registry entry.
        /// </summary>
        /// <returns>A capitalised chipset name or null.</returns>
        public string ChipsetModelMatcher()
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

        /// <summary>
        /// Attempt to read the currently installed AMD Chipset Software version.
        /// </summary>
        /// <returns>A currently installed package version or null.</returns>
        public string GetInstalledAMDChipsetVersion()
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

        /// <summary>
        /// Attempt to fetch the latest release version URL for AMD Chipset Software.
        /// </summary>
        /// <param name="chipset">Target chipset to check.</param>
        /// <returns>The latest release version or null.</returns>
        public async Task<string> FetchLatestVersionUrl(string chipset)
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
                    string chipsetUrlPattern = $@"https://[^""]+\.exe";
                    Match chipsetUrlMatch = Regex.Match(chipsetPageContent, chipsetUrlPattern, RegexOptions.IgnoreCase);
                    if (chipsetUrlMatch.Success)
                    {
                        return chipsetUrlMatch.Value;
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private async void InstallDrivers_Click(object sender, RoutedEventArgs e)
        {
            string localFilePath = Path.Combine(Path.GetTempPath(), $"amd_chipset_software_{this.latestVersion}.exe");
            try
            {
                this.InstallDriversButton.IsEnabled = false;
                this.InstallDriversButton.Visibility = Visibility.Collapsed;
                this.DownloadProgressBar.Visibility = Visibility.Visible;
                this.CancelButton.Visibility = Visibility.Visible;
                this.cancellationTokenSource = new CancellationTokenSource();

                await this.DownloadFileAsync(this.latestVersionUrl, localFilePath, this.cancellationTokenSource.Token);

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
                await Task.Run(() => this.MonitorProcess(process));
                await Task.Run(() => process.WaitForExit());
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

        private async Task DownloadFileAsync(string requestUri, string destinationFilePath, CancellationToken cancellationToken)
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

        private async Task MonitorProcess(Process process)
        {
            while (!process.HasExited)
            {
                await this.Dispatcher.InvokeAsync(() => this.SetInstalledVersion());
                await Task.Delay(1000);
            }
        }

        private void SetInstalledVersion()
        {
            this.installedVersion = this.GetInstalledAMDChipsetVersion();
            this.InstalledVersionText.Text = this.installedVersion ?? "Not Installed";
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
