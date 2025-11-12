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
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace frmmenuclassic
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Source - https://stackoverflow.com/a
            // Posted by Rovann Linhalis
            // Retrieved 2025-11-12, License - CC BY-SA 4.0

            string osCaption = "";

            // Cần có tham chiếu (Reference) đến System.Management
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
            {
                ManagementObjectCollection information = searcher.Get();
                if (information != null)
                {
                    foreach (ManagementObject obj in information)
                    {
                        // Lấy toàn bộ Caption, ví dụ: "Microsoft Windows 11 Home"
                        osCaption = obj["Caption"]?.ToString() ?? "";

                        // Hiển thị thông tin OS lên label (có thể bỏ qua nếu không cần)
                        string osArchitecture = obj["OSArchitecture"]?.ToString() ?? "Unknown Architecture";
                        lblos.Text = osCaption + " - " + osArchitecture;
                    }
                }
            }

            // --- BƯỚC KIỂM TRA: Chỉ tiếp tục nếu là Windows 11 ---

            // Dùng .ToLower() để kiểm tra không phân biệt chữ hoa/chữ thường (ví dụ: "Windows 11" hay "windows 11")
            if (!osCaption.ToLower().Contains("windows 11"))
            {
                // 1. Thông báo lỗi và hệ điều hành hiện tại
                MessageBox.Show(
                    $"Current Operating System ({osCaption}) is not Windows 11. Application will close!",
                    "System Error!!",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                // 2. Out (Đóng form, dẫn đến ứng dụng đóng)
                this.Close();
                return; // Đảm bảo không thực hiện code tiếp theo
            }

            // --- CÔNG VIỆC TIẾP THEO (Chỉ chạy khi là Windows 11) ---

            // Nếu là Windows 11, thực hiện cv tiếp theo
            // Ví dụ: lblStatus.Text = "Hệ điều hành hợp lệ. Tiếp tục công việc...";            }
            string keyName = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
            using (RegistryKey rkSubKey = Registry.CurrentUser.OpenSubKey(keyName, true))
            {
                //xóa khóa nếu tồn tại
                if (rkSubKey != null)
                {
                    btnRestoreDefault.Enabled = true;
                }
                else
                {
                    btnEnableClassic.Enabled = true;
                }    
            }
        }

        //HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32


        public static void RestartExplorer()
    {
        foreach (Process p in Process.GetProcesses())
        {
            if (p.ProcessName == "explorer")
            {
                p.Kill();
            }
        }
        Process.Start("explorer.exe"); // Start a new instance
    }
    private void btnEnableClassic_Click(object sender, EventArgs e)
        {
            string keyName = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

            // Sử dụng 'using' để đảm bảo tài nguyên được giải phóng
            using (RegistryKey rkSubKey = Registry.CurrentUser.OpenSubKey(keyName, true))
            {
                if (rkSubKey == null)
                {
                    // Khóa không tồn tại, tạo nó.
                    // Bạn chỉ cần tạo khóa InprocServer32, nó sẽ tự động tạo các khóa cha
                    using (RegistryKey newKey = Registry.CurrentUser.CreateSubKey(keyName))
                    {
                        // Tùy chọn: Đảm bảo Giá trị Mặc định (Default Value) là rỗng
                        newKey.SetValue("", "");
                        MessageBox.Show("Registry Key have been created.","Infomation",MessageBoxButtons.OK,MessageBoxIcon.Information);
                        // Khởi động lại Explorer để áp dụng thay đổi
                        RestartExplorer();
                        btnEnableClassic.Enabled = false;
                        Application.Exit();
                    }
                }
                else
                {
                    MessageBox.Show("Registry existed!", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Exit();
                }
            }
        }
        //"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}
        private void btnRestoreDefault_Click(object sender, EventArgs e)
        {
            string keyName = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
            using (RegistryKey rkSubKey = Registry.CurrentUser.OpenSubKey(keyName, true))
            {
                //xóa khóa nếu tồn tại
                if (rkSubKey != null)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(keyName);
                    MessageBox.Show("Registry key successfully deleted!","Infomation!",MessageBoxButtons.OK,MessageBoxIcon.Information);
                    // Khởi động lại Explorer để áp dụng thay đổi
                    string explorer = string.Format("{0}\\{1}", Environment.GetEnvironmentVariable("WINDIR"), "explorer.exe");
                    Process process = new Process();
                    process.StartInfo.FileName = explorer;
                    process.StartInfo.UseShellExecute = true;
                    process.Start();
                    btnRestoreDefault.Enabled = false;
                    Application.Exit();
                }
                else
                {
                    MessageBox.Show("Registry does not exist!","Information",MessageBoxButtons.OK,MessageBoxIcon.Information);
                    Application.Exit();
                }
            }
        }
    }
}
