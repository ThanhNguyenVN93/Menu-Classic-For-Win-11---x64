using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using frmmenuclassic.Services;

namespace frmmenuclassic
{
    public partial class Form1 : Form
    {
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/ThanhNguyenVN93/Menu-Classic-For-Win-11---x64/releases/latest";

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            UpdateService.CleanupOldBackup(Application.ExecutablePath);
            UpdateService.EnableTls12();

            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            this.Text = string.Format("Menu Classic For Win 11 - {0}  v{1}.{2}.{3}",
                UpdateService.Arch, ver.Major, ver.Minor, ver.Build);

            // Source - https://stackoverflow.com/a
            // Posted by Rovann Linhalis
            // Retrieved 2025-11-12, License - CC BY-SA 4.0
            string osCaption = "";

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
            {
                ManagementObjectCollection information = searcher.Get();
                if (information != null)
                {
                    foreach (ManagementObject obj in information)
                    {
                        osCaption = obj["Caption"]?.ToString() ?? "";
                        string osArchitecture = obj["OSArchitecture"]?.ToString() ?? "Unknown Architecture";
                        lblos.Text = osCaption + " - " + osArchitecture;
                    }
                }
            }

            bool isWin11 = osCaption.ToLower().Contains("windows 11");
            if (!isWin11)
            {
                try
                {
                    using (RegistryKey ver2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        int build;
                        if (ver2 != null && int.TryParse(ver2.GetValue("CurrentBuildNumber")?.ToString(), out build) && build >= 22000)
                            isWin11 = true;
                    }
                }
                catch { }
            }

            if (!isWin11)
            {
                MessageBox.Show(
                    $"Current Operating System ({osCaption}) is not Windows 11. Application will close!",
                    "System Error!!",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                this.Close();
                return;
            }

            using (RegistryKey hkcu = OpenCurrentUser())
            {
                bool classicEnabled = IsClassicMenuEnabled(hkcu);
                if (classicEnabled)
                {
                    btnRestoreDefault.Enabled = true;
                    Log("Classic menu is currently ENABLED.");
                }
                else
                {
                    btnEnableClassic.Enabled = true;
                    Log("Classic menu is currently DISABLED.");
                }
            }
        }

        private const string PrimaryClsid = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        private const string SecondaryClsid = @"Software\Classes\CLSID\{51668040-5e31-4023-bbe3-149163bd80f1}";

        private static RegistryView TargetRegistryView =>
            Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;

        private static RegistryKey OpenCurrentUser()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, TargetRegistryView);
        }

        private static bool RequiresSecondaryClsid()
        {
            using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, TargetRegistryView))
            using (RegistryKey key = hklm.OpenSubKey(SecondaryClsid, false))
            {
                return key != null;
            }
        }

        private static bool HasInprocOverride(RegistryKey hkcu, string clsidPath)
        {
            using (RegistryKey rkSubKey = hkcu.OpenSubKey(clsidPath + @"\InprocServer32", false))
            {
                return rkSubKey != null && rkSubKey.GetValue("") != null;
            }
        }

        private static bool IsClassicMenuEnabled(RegistryKey hkcu)
        {
            if (!HasInprocOverride(hkcu, PrimaryClsid)) return false;
            if (RequiresSecondaryClsid() && !HasInprocOverride(hkcu, SecondaryClsid)) return false;
            return true;
        }

        private async void Form1_Shown(object sender, EventArgs e)
        {
            await RunUpdateCheckAsync(silent: true);
        }

        private void Log(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => Log(message)));
                return;
            }
            txtLog.AppendText(message + Environment.NewLine);
        }

        private async Task RunUpdateCheckAsync(bool silent)
        {
            try
            {
                Log("Checking for updates...");
                string json = await Task.Run(() => DownloadString(ReleasesApiUrl));
                var info = UpdateService.ParseRelease(json, UpdateService.Arch);
                if (info == null)
                {
                    if (!silent) Log("No applicable release found.");
                    else Log("No update available.");
                    return;
                }

                var currentVer = Assembly.GetExecutingAssembly().GetName().Version;
                if (!UpdateService.IsNewer(currentVer, info.Version))
                {
                    Log(string.Format("You are up to date (v{0}).", currentVer));
                    return;
                }

                Log(string.Format("New version available: v{0}", info.Version));

                var answer = MessageBox.Show(
                    string.Format("Version {0} is available. Download and install now?", info.Version),
                    "Update Available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (answer != DialogResult.Yes) return;

                await DownloadAndApplyUpdateAsync(info);
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show("Update check failed: " + ex.Message, "Update Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    Log("Update check failed: " + ex.Message);
            }
        }

        private async Task DownloadAndApplyUpdateAsync(UpdateService.ReleaseInfo info)
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), "frmmenuclassic_update_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try
            {
                string zipPath = Path.Combine(tmpDir, info.ZipFileName);
                string sha256Path = Path.Combine(tmpDir, "SHA256SUMS.txt");

                Log("Downloading " + info.ZipFileName + "...");
                await Task.Run(() => DownloadFile(info.ZipUrl, zipPath));

                Log("Downloading SHA256SUMS.txt...");
                await Task.Run(() => DownloadFile(info.Sha256Url, sha256Path));

                string sha256Content = File.ReadAllText(sha256Path);
                string expected = UpdateService.ParseSha256(sha256Content, info.ZipFileName);
                if (expected == null)
                    throw new InvalidOperationException("Could not find SHA-256 entry for " + info.ZipFileName);

                Log("Verifying SHA-256...");
                bool ok = await Task.Run(() => UpdateService.VerifySha256(zipPath, expected));
                if (!ok)
                    throw new InvalidOperationException("SHA-256 verification failed.");

                Log("Extracting...");
                string extractDir = Path.Combine(tmpDir, "extracted");
                Directory.CreateDirectory(extractDir);
                string newExe = await Task.Run(() => UpdateService.ExtractExe(zipPath, extractDir));
                if (newExe == null)
                    throw new InvalidOperationException("No .exe found in archive.");

                Log("Applying update...");
                UpdateService.ApplyStagedUpdate(Application.ExecutablePath, newExe);
            }
            catch
            {
                try { Directory.Delete(tmpDir, true); } catch { }
                throw;
            }
        }

        private static string DownloadString(string url)
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "frmmenuclassic-updater/1.2";
                return client.DownloadString(url);
            }
        }

        private static void DownloadFile(string url, string dest)
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "frmmenuclassic-updater/1.2";
                client.DownloadFile(url, dest);
            }
        }

        public static void RestartExplorer()
        {
            foreach (Process p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
            System.Threading.Thread.Sleep(500);
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }

        private async void btnCheckUpdate_Click(object sender, EventArgs e)
        {
            btnCheckUpdate.Enabled = false;
            try
            {
                await RunUpdateCheckAsync(silent: false);
            }
            finally
            {
                btnCheckUpdate.Enabled = true;
            }
        }

        private void btnEnableClassic_Click(object sender, EventArgs e)
        {
            using (RegistryKey hkcu = OpenCurrentUser())
            {
                if (IsClassicMenuEnabled(hkcu))
                {
                    MessageBox.Show("Registry existed!", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Exit();
                    return;
                }

                using (RegistryKey primaryKey = hkcu.CreateSubKey(PrimaryClsid + @"\InprocServer32"))
                {
                    primaryKey.SetValue("", "");
                }

                if (RequiresSecondaryClsid())
                {
                    using (RegistryKey secondaryKey = hkcu.CreateSubKey(SecondaryClsid + @"\InprocServer32"))
                    {
                        secondaryKey.SetValue("", "");
                    }
                }

                MessageBox.Show("Registry Key have been created.", "Infomation", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RestartExplorer();
                btnEnableClassic.Enabled = false;
                Application.Exit();
            }
        }

        private void btnRestoreDefault_Click(object sender, EventArgs e)
        {
            using (RegistryKey hkcu = OpenCurrentUser())
            {
                bool deletedAny = false;
                foreach (string clsidPath in new[] { PrimaryClsid, SecondaryClsid })
                {
                    using (RegistryKey rkSubKey = hkcu.OpenSubKey(clsidPath, false))
                    {
                        if (rkSubKey != null)
                        {
                            hkcu.DeleteSubKeyTree(clsidPath, false);
                            deletedAny = true;
                        }
                    }
                }

                if (deletedAny)
                {
                    MessageBox.Show("Registry key successfully deleted!", "Infomation!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RestartExplorer();
                    btnRestoreDefault.Enabled = false;
                    Application.Exit();
                }
                else
                {
                    MessageBox.Show("Registry does not exist!", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Exit();
                }
            }
        }
    }
}
