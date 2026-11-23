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

        private AppLang _currentLang = Localization.LoadLanguage();
        private readonly List<KeyValuePair<string, object[]>> _logEntries = new List<KeyValuePair<string, object[]>>();

        public Form1()
        {
            InitializeComponent();
            ApplyLanguage();
        }

        private string T(string key, params object[] args)
        {
            return Localization.Get(_currentLang, key, args);
        }

        private void ApplyLanguage()
        {
            this.SuspendLayout();
            try
            {
                label1.Text = T("TitleHeader");
                btnEnableClassic.Text = T("BtnClassic");
                btnRestoreDefault.Text = T("BtnModern");
                btnCheckUpdate.Text = T("BtnUpdate");
                btnLangToggle.Text = T("BtnLang");

                var sb = new StringBuilder();
                foreach (var entry in _logEntries)
                    sb.Append(Localization.Get(_currentLang, entry.Key, entry.Value)).Append(Environment.NewLine);
                txtLog.Text = sb.ToString();
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
            finally
            {
                this.ResumeLayout(true);
            }
        }

        private void btnLangToggle_Click(object sender, EventArgs e)
        {
            _currentLang = _currentLang == AppLang.En ? AppLang.Vi : AppLang.En;
            Localization.SaveLanguage(_currentLang);
            ApplyLanguage();
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
                        lblos.Text = FormatShortOsLabel(osCaption, osArchitecture);
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
                    T("NotWin11Body", osCaption),
                    T("NotWin11Title"),
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
                    Log("StatusEnabled");
                }
                else
                {
                    btnEnableClassic.Enabled = true;
                    Log("StatusDisabled");
                }
            }
        }

        private static string FormatShortOsLabel(string osCaption, string osArchitecture)
        {
            string shortOs = osCaption ?? "";
            string lower = shortOs.ToLowerInvariant();
            if (lower.Contains("windows 11")) shortOs = "MS Windows 11";
            else if (lower.Contains("windows 10")) shortOs = "MS Windows 10";
            else if (lower.Contains("windows 8.1")) shortOs = "MS Windows 8.1";
            else if (lower.Contains("windows 8")) shortOs = "MS Windows 8";
            else if (lower.Contains("windows 7")) shortOs = "MS Windows 7";
            else if (lower.Contains("microsoft windows")) shortOs = shortOs.Replace("Microsoft Windows", "MS Windows");

            return string.IsNullOrWhiteSpace(osArchitecture) ? shortOs : (shortOs + " - " + osArchitecture);
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

        private void Log(string key, params object[] args)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => Log(key, args)));
                return;
            }
            _logEntries.Add(new KeyValuePair<string, object[]>(key, args));
            txtLog.AppendText(T(key, args) + Environment.NewLine);
        }

        private async Task RunUpdateCheckAsync(bool silent)
        {
            try
            {
                Log("CheckingUpdates");
                string json = await Task.Run(() => DownloadString(ReleasesApiUrl));
                var info = UpdateService.ParseRelease(json, UpdateService.Arch);
                if (info == null)
                {
                    if (!silent) Log("NoApplicableRelease");
                    else Log("NoUpdateAvailable");
                    return;
                }

                var currentVer = Assembly.GetExecutingAssembly().GetName().Version;
                if (!UpdateService.IsNewer(currentVer, info.Version))
                {
                    Log("UpToDate", currentVer);
                    return;
                }

                Log("NewVersionFound", info.Version);

                var answer = MessageBox.Show(
                    T("UpdatePromptBody", info.Version),
                    T("UpdatePromptTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (answer != DialogResult.Yes) return;

                await DownloadAndApplyUpdateAsync(info);
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show(T("UpdateFailed", ex.Message), T("UpdateErrorTitle"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    Log("UpdateFailed", ex.Message);
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

                Log("Downloading", info.ZipFileName);
                await Task.Run(() => DownloadFile(info.ZipUrl, zipPath));

                Log("Downloading", "SHA256SUMS.txt");
                await Task.Run(() => DownloadFile(info.Sha256Url, sha256Path));

                string sha256Content = File.ReadAllText(sha256Path);
                string expected = UpdateService.ParseSha256(sha256Content, info.ZipFileName);
                if (expected == null)
                    throw new InvalidOperationException(T("MissingShaEntry", info.ZipFileName));

                Log("VerifyingSha256");
                bool ok = await Task.Run(() => UpdateService.VerifySha256(zipPath, expected));
                if (!ok)
                    throw new InvalidOperationException(T("ShaFailed"));

                Log("Extracting");
                string extractDir = Path.Combine(tmpDir, "extracted");
                Directory.CreateDirectory(extractDir);
                string newExe = await Task.Run(() => UpdateService.ExtractExe(zipPath, extractDir));
                if (newExe == null)
                    throw new InvalidOperationException(T("MissingExeInZip"));

                Log("ApplyingUpdate");
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
                    MessageBox.Show(T("RegExisted"), T("InfoTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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

                MessageBox.Show(T("RegCreated"), T("InfoTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    MessageBox.Show(T("RegDeleted"), T("InfoTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RestartExplorer();
                    btnRestoreDefault.Enabled = false;
                    Application.Exit();
                }
                else
                {
                    MessageBox.Show(T("RegNotExist"), T("InfoTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Exit();
                }
            }
        }
    }
}
