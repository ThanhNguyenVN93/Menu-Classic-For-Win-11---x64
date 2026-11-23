using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace frmmenuclassic.Services
{
    public static class UpdateService
    {
        private const string AllowedAssetPrefix =
            "https://github.com/ThanhNguyenVN93/Menu-Classic-For-Win-11---x64/releases/download/";

        public static string Arch =>
            Environment.Is64BitProcess ? "x64" : "x86";

        public static void EnableTls12()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // Tls12
        }

        private static bool IsAllowedAssetUrl(string url, bool allowLocalhostForTests = false)
        {
            if (url == null) return false;
            if (url.StartsWith(AllowedAssetPrefix, StringComparison.Ordinal)) return true;
            if (allowLocalhostForTests &&
                (url.StartsWith("http://127.0.0.1", StringComparison.Ordinal) ||
                 url.StartsWith("http://localhost", StringComparison.Ordinal)))
                return true;
            return false;
        }

        public static Version ParseVersion(string tag)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            string s = tag.TrimStart('v', 'V');
            return new Version(s);
        }

        public static bool IsNewer(Version current, Version latest)
        {
            return latest > current;
        }

        // --- GitHub Releases JSON contracts ---

        [DataContract]
        private class GhRelease
        {
            [DataMember(Name = "tag_name")]   public string TagName    { get; set; }
            [DataMember(Name = "draft")]      public bool   Draft      { get; set; }
            [DataMember(Name = "prerelease")] public bool   Prerelease { get; set; }
            [DataMember(Name = "assets")]     public List<GhAsset> Assets { get; set; }
        }

        [DataContract]
        private class GhAsset
        {
            [DataMember(Name = "name")]                 public string Name               { get; set; }
            [DataMember(Name = "browser_download_url")] public string BrowserDownloadUrl { get; set; }
        }

        public class ReleaseInfo
        {
            public Version  Version       { get; set; }
            public string   ZipUrl        { get; set; }
            public string   Sha256Url     { get; set; }
            public string   ZipFileName   { get; set; }
        }

        public static ReleaseInfo ParseRelease(string json, string arch, bool allowLocalhostForTests = false)
        {
            var ser = new DataContractJsonSerializer(typeof(GhRelease));
            GhRelease rel;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                rel = (GhRelease)ser.ReadObject(ms);

            if (rel.Draft || rel.Prerelease) return null;

            Version ver = ParseVersion(rel.TagName);
            string verStr = rel.TagName.TrimStart('v', 'V');
            string expectedZip = string.Format("MenuClassicForWin11-v{0}-win-{1}.zip", verStr, arch);

            string zipUrl  = null;
            string sha256Url = null;

            if (rel.Assets != null)
            {
                foreach (var asset in rel.Assets)
                {
                    if (asset.Name == expectedZip &&
                        IsAllowedAssetUrl(asset.BrowserDownloadUrl, allowLocalhostForTests))
                        zipUrl = asset.BrowserDownloadUrl;

                    if (asset.Name == "SHA256SUMS.txt" &&
                        IsAllowedAssetUrl(asset.BrowserDownloadUrl, allowLocalhostForTests))
                        sha256Url = asset.BrowserDownloadUrl;
                }
            }

            if (zipUrl == null || sha256Url == null) return null;

            return new ReleaseInfo
            {
                Version     = ver,
                ZipUrl      = zipUrl,
                Sha256Url   = sha256Url,
                ZipFileName = expectedZip,
            };
        }

        public static string ParseSha256(string sha256SumsContent, string fileName)
        {
            if (sha256SumsContent == null) return null;
            foreach (var line in sha256SumsContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // Format: <hash>  <filename>  or  <hash> *<filename>
                var parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                string name = parts[1].TrimStart('*', ' ');
                if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                    return parts[0].Trim().ToUpperInvariant();
            }
            return null;
        }

        public static bool VerifySha256(string filePath, string expectedHex)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(fs);
                string actual = BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                return string.Equals(actual, expectedHex.ToUpperInvariant(), StringComparison.Ordinal);
            }
        }

        public static string ExtractExe(string zipPath, string destDir, long maxBytes = 50 * 1024 * 1024)
        {
            string fullDest = Path.GetFullPath(destDir);
            if (!fullDest.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullDest += Path.DirectorySeparatorChar;

            string extractedExe = null;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string destPath = Path.GetFullPath(Path.Combine(fullDest, entry.FullName));
                    if (!destPath.StartsWith(fullDest, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Zip-slip path traversal detected: " + entry.FullName);

                    if (entry.Length > maxBytes)
                        throw new InvalidOperationException(
                            string.Format("Entry '{0}' exceeds size limit of {1} bytes.", entry.FullName, maxBytes));

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                    entry.ExtractToFile(destPath, overwrite: true);
                    extractedExe = destPath;
                }
            }
            return extractedExe;
        }

        public static void ApplyStagedUpdate(string currentExePath, string newExePath)
        {
            string oldPath = currentExePath + ".old";
            if (File.Exists(oldPath))
                File.Delete(oldPath);

            File.Move(currentExePath, oldPath);
            File.Copy(newExePath, currentExePath, overwrite: false);

            Process.Start(new System.Diagnostics.ProcessStartInfo(currentExePath) { UseShellExecute = true });
            System.Windows.Forms.Application.Exit();
        }

        public static void CleanupOldBackup(string currentExePath)
        {
            string oldPath = currentExePath + ".old";
            if (File.Exists(oldPath))
            {
                try { File.Delete(oldPath); } catch { }
            }
        }
    }
}
