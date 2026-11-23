using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using frmmenuclassic.Services;

namespace SelfTest
{
    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        static int Main()
        {
            Test_ParseVersion();
            Test_IsNewer();
            Test_ParseSha256();
            Test_VerifySha256();
            Test_ParseRelease();
            Test_ExtractExe_Normal();
            Test_ExtractExe_ZipSlip();
            Test_ExtractExe_SizeLimit();

            Console.WriteLine();
            Console.WriteLine(string.Format("Results: {0} passed, {1} failed.", _pass, _fail));
            return _fail == 0 ? 0 : 1;
        }

        // --- helpers ---

        private static void Pass(string name)
        {
            _pass++;
            Console.WriteLine("[PASS] " + name);
        }

        private static void Fail(string name, string reason)
        {
            _fail++;
            Console.WriteLine("[FAIL] " + name + " — " + reason);
        }

        private static void Assert(bool condition, string testName, string failMsg = "assertion failed")
        {
            if (condition) Pass(testName);
            else Fail(testName, failMsg);
        }

        private static void AssertThrows<T>(Action action, string testName) where T : Exception
        {
            try
            {
                action();
                Fail(testName, "expected " + typeof(T).Name + " but no exception thrown");
            }
            catch (T)
            {
                Pass(testName);
            }
            catch (Exception ex)
            {
                Fail(testName, "expected " + typeof(T).Name + " but got " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // --- ParseVersion ---

        private static void Test_ParseVersion()
        {
            var v = UpdateService.ParseVersion("v1.2.3");
            Assert(v.Major == 1 && v.Minor == 2 && v.Build == 3, "ParseVersion_v1.2.3");

            v = UpdateService.ParseVersion("V2.0.0.1");
            Assert(v.Major == 2 && v.Minor == 0 && v.Build == 0 && v.Revision == 1, "ParseVersion_V2.0.0.1");

            v = UpdateService.ParseVersion("1.0.0");
            Assert(v.Major == 1 && v.Minor == 0 && v.Build == 0, "ParseVersion_no_prefix");

            AssertThrows<ArgumentNullException>(() => UpdateService.ParseVersion(null), "ParseVersion_null");
        }

        // --- IsNewer ---

        private static void Test_IsNewer()
        {
            Assert(UpdateService.IsNewer(new Version(1, 0, 0), new Version(1, 1, 0)), "IsNewer_true");
            Assert(!UpdateService.IsNewer(new Version(1, 1, 0), new Version(1, 0, 0)), "IsNewer_false_older");
            Assert(!UpdateService.IsNewer(new Version(1, 1, 0), new Version(1, 1, 0)), "IsNewer_false_equal");
        }

        // --- ParseSha256 ---

        private static void Test_ParseSha256()
        {
            string content = "abc123  file.zip\ndef456 *other.zip\n";
            Assert(UpdateService.ParseSha256(content, "file.zip") == "ABC123", "ParseSha256_found");
            Assert(UpdateService.ParseSha256(content, "other.zip") == "DEF456", "ParseSha256_star_prefix");
            Assert(UpdateService.ParseSha256(content, "missing.zip") == null, "ParseSha256_not_found");
            Assert(UpdateService.ParseSha256(null, "file.zip") == null, "ParseSha256_null_content");
        }

        // --- VerifySha256 ---

        private static void Test_VerifySha256()
        {
            string tmpFile = Path.GetTempFileName();
            try
            {
                byte[] data = Encoding.UTF8.GetBytes("hello world");
                File.WriteAllBytes(tmpFile, data);

                string expectedHex;
                using (var sha = SHA256.Create())
                    expectedHex = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToUpperInvariant();

                Assert(UpdateService.VerifySha256(tmpFile, expectedHex), "VerifySha256_match");
                Assert(!UpdateService.VerifySha256(tmpFile, "0000000000000000000000000000000000000000000000000000000000000000"), "VerifySha256_mismatch");
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        // --- ParseRelease ---

        private static string MakeJson(string tag, bool draft, bool prerelease, string zipUrl, string sha256Url)
        {
            return string.Format(
                "{{\"tag_name\":\"{0}\",\"draft\":{1},\"prerelease\":{2},\"assets\":[" +
                "{{\"name\":\"MenuClassicForWin11-v{3}-win-x64.zip\",\"browser_download_url\":\"{4}\"}}," +
                "{{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"{5}\"}}" +
                "]}}",
                tag,
                draft  ? "true" : "false",
                prerelease ? "true" : "false",
                tag.TrimStart('v', 'V'),
                zipUrl,
                sha256Url);
        }

        private const string BaseUrl =
            "https://github.com/ThanhNguyenVN93/Menu-Classic-For-Win-11---x64/releases/download/v1.3.0/";

        private static void Test_ParseRelease()
        {
            string zipUrl    = BaseUrl + "MenuClassicForWin11-v1.3.0-win-x64.zip";
            string sha256Url = BaseUrl + "SHA256SUMS.txt";

            string json = MakeJson("v1.3.0", false, false, zipUrl, sha256Url);
            var info = UpdateService.ParseRelease(json, "x64");
            Assert(info != null && info.Version.Minor == 3, "ParseRelease_valid");

            string draftJson = MakeJson("v1.3.0", true, false, zipUrl, sha256Url);
            Assert(UpdateService.ParseRelease(draftJson, "x64") == null, "ParseRelease_draft_rejected");

            string preJson = MakeJson("v1.3.0", false, true, zipUrl, sha256Url);
            Assert(UpdateService.ParseRelease(preJson, "x64") == null, "ParseRelease_prerelease_rejected");

            // localhost allowed via flag
            string localZip    = "http://127.0.0.1/MenuClassicForWin11-v1.3.0-win-x64.zip";
            string localSha256 = "http://127.0.0.1/SHA256SUMS.txt";
            string localJson   = MakeJson("v1.3.0", false, false, localZip, localSha256);
            Assert(UpdateService.ParseRelease(localJson, "x64", allowLocalhostForTests: true) != null,
                "ParseRelease_localhost_allowed");
            Assert(UpdateService.ParseRelease(localJson, "x64", allowLocalhostForTests: false) == null,
                "ParseRelease_localhost_rejected");
        }

        // --- ExtractExe ---

        private static string CreateZipWithExe(string dir, string entryName, byte[] content)
        {
            string zipPath = Path.Combine(dir, "test.zip");
            using (var fs = File.Create(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(entryName);
                using (var s = entry.Open())
                    s.Write(content, 0, content.Length);
            }
            return zipPath;
        }

        private static void Test_ExtractExe_Normal()
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), "selftest_extract_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try
            {
                byte[] exeBytes = Encoding.UTF8.GetBytes("MZ fake exe");
                string zipPath = CreateZipWithExe(tmpDir, "app.exe", exeBytes);
                string outDir  = Path.Combine(tmpDir, "out");
                Directory.CreateDirectory(outDir);

                string result = UpdateService.ExtractExe(zipPath, outDir);
                Assert(result != null && File.Exists(result), "ExtractExe_normal");
            }
            finally
            {
                Directory.Delete(tmpDir, true);
            }
        }

        private static void Test_ExtractExe_ZipSlip()
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), "selftest_zipslip_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try
            {
                byte[] exeBytes = Encoding.UTF8.GetBytes("MZ fake exe");
                // path traversal entry
                string zipPath = CreateZipWithExe(tmpDir, "../../malicious.exe", exeBytes);
                string outDir  = Path.Combine(tmpDir, "out");
                Directory.CreateDirectory(outDir);

                AssertThrows<InvalidOperationException>(
                    () => UpdateService.ExtractExe(zipPath, outDir),
                    "ExtractExe_zipslip_rejected");
            }
            finally
            {
                Directory.Delete(tmpDir, true);
            }
        }

        private static void Test_ExtractExe_SizeLimit()
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), "selftest_sizelimit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try
            {
                // create a zip where the entry claims a large uncompressed size
                // We'll just use a 1-byte limit and a 2-byte file
                byte[] exeBytes = new byte[] { 0x4D, 0x5A }; // "MZ"
                string zipPath = CreateZipWithExe(tmpDir, "app.exe", exeBytes);
                string outDir  = Path.Combine(tmpDir, "out");
                Directory.CreateDirectory(outDir);

                AssertThrows<InvalidOperationException>(
                    () => UpdateService.ExtractExe(zipPath, outDir, maxBytes: 1),
                    "ExtractExe_sizeLimit_rejected");
            }
            finally
            {
                Directory.Delete(tmpDir, true);
            }
        }
    }
}
