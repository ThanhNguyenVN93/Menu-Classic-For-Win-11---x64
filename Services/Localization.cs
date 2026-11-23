using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace frmmenuclassic.Services
{
    public enum AppLang { En, Vi }

    public static class Localization
    {
        private const string RegPath = @"Software\MenuClassicForWin11";
        private const string RegValue = "Language";

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "TitleHeader", "CLASSIC RIGHT-CLICK MENU" },
            { "BtnClassic", "CLASSIC MENU" },
            { "BtnModern", "MODERN MENU" },
            { "BtnUpdate", "CHECK UPDATE" },
            { "BtnLang", "[ENG] / VI" },
            { "StatusEnabled", "Classic menu is currently ENABLED." },
            { "StatusDisabled", "Classic menu is currently DISABLED." },
            { "CheckingUpdates", "Checking for updates..." },
            { "NoApplicableRelease", "No applicable release found." },
            { "NoUpdateAvailable", "No update available." },
            { "UpToDate", "You are up to date (v{0})." },
            { "NewVersionFound", "New version available: v{0}" },
            { "UpdatePromptTitle", "Update Available" },
            { "UpdatePromptBody", "Version {0} is available. Download and install now?" },
            { "Downloading", "Downloading {0}..." },
            { "VerifyingSha256", "Verifying SHA-256..." },
            { "Extracting", "Extracting..." },
            { "ApplyingUpdate", "Applying update..." },
            { "UpdateErrorTitle", "Update Error" },
            { "UpdateFailed", "Update check failed: {0}" },
            { "NotWin11Title", "System Error!!" },
            { "NotWin11Body", "Current Operating System ({0}) is not Windows 11. Application will close!" },
            { "RegCreated", "Registry Key have been created." },
            { "RegExisted", "Registry existed!" },
            { "RegDeleted", "Registry key successfully deleted!" },
            { "RegNotExist", "Registry does not exist!" },
            { "InfoTitle", "Information" },
            { "MissingShaEntry", "Could not find SHA-256 entry for {0}" },
            { "ShaFailed", "SHA-256 verification failed." },
            { "MissingExeInZip", "No .exe found in archive." },
        };

        private static readonly Dictionary<string, string> Vi = new Dictionary<string, string>
        {
            { "TitleHeader", "MENU CHUỘT PHẢI CỔ ĐIỂN" },
            { "BtnClassic", "MENU CỔ ĐIỂN" },
            { "BtnModern", "MENU HIỆN ĐẠI" },
            { "BtnUpdate", "KIỂM TRA CẬP NHẬT" },
            { "BtnLang", "ENG / [VI]" },
            { "StatusEnabled", "Menu cổ điển hiện đang BẬT." },
            { "StatusDisabled", "Menu cổ điển hiện đang TẮT." },
            { "CheckingUpdates", "Đang kiểm tra cập nhật..." },
            { "NoApplicableRelease", "Không tìm thấy bản phát hành phù hợp." },
            { "NoUpdateAvailable", "Không có bản cập nhật." },
            { "UpToDate", "Bạn đang dùng bản mới nhất (v{0})." },
            { "NewVersionFound", "Có phiên bản mới: v{0}" },
            { "UpdatePromptTitle", "Có bản cập nhật" },
            { "UpdatePromptBody", "Đã có phiên bản {0}. Tải về và cài đặt ngay?" },
            { "Downloading", "Đang tải {0}..." },
            { "VerifyingSha256", "Đang xác minh SHA-256..." },
            { "Extracting", "Đang giải nén..." },
            { "ApplyingUpdate", "Đang áp dụng bản cập nhật..." },
            { "UpdateErrorTitle", "Lỗi cập nhật" },
            { "UpdateFailed", "Kiểm tra cập nhật thất bại: {0}" },
            { "NotWin11Title", "Lỗi hệ thống!!" },
            { "NotWin11Body", "Hệ điều hành hiện tại ({0}) không phải Windows 11. Ứng dụng sẽ đóng!" },
            { "RegCreated", "Đã tạo khóa Registry." },
            { "RegExisted", "Registry đã tồn tại!" },
            { "RegDeleted", "Đã xóa khóa Registry thành công!" },
            { "RegNotExist", "Registry không tồn tại!" },
            { "InfoTitle", "Thông tin" },
            { "MissingShaEntry", "Không tìm thấy mục SHA-256 cho {0}" },
            { "ShaFailed", "Xác minh SHA-256 thất bại." },
            { "MissingExeInZip", "Không tìm thấy tệp .exe trong gói nén." },
        };

        public static string Get(AppLang lang, string key, params object[] args)
        {
            var table = lang == AppLang.Vi ? Vi : En;
            string s;
            if (!table.TryGetValue(key, out s) && !En.TryGetValue(key, out s))
                return key;
            if (args == null || args.Length == 0) return s;
            return string.Format(CultureInfo.CurrentCulture, s, args);
        }

        /// <summary>Returns keys present in one table but missing in the other (empty = parity).</summary>
        public static List<string> ValidateKeyParity()
        {
            var missing = new List<string>();
            foreach (var k in En.Keys)
                if (!Vi.ContainsKey(k)) missing.Add("vi:" + k);
            foreach (var k in Vi.Keys)
                if (!En.ContainsKey(k)) missing.Add("en:" + k);
            return missing;
        }

        public static AppLang LoadLanguage()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegPath, false))
                {
                    var v = key == null ? null : key.GetValue(RegValue) as string;
                    if (string.Equals(v, "vi", StringComparison.OrdinalIgnoreCase))
                        return AppLang.Vi;
                }
            }
            catch { }
            return AppLang.En;
        }

        public static void SaveLanguage(AppLang lang)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    key.SetValue(RegValue, lang == AppLang.Vi ? "vi" : "en");
                }
            }
            catch { }
        }
    }
}
