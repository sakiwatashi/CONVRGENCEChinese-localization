// CONVRGENCE 繁體中文化安裝程式
// 編譯：installer\build.ps1（用 Windows 內建的 .NET Framework csc，C# 5 語法）
// 安裝內容是內嵌的 payload.zip，解壓縮到遊戲資料夾。
//
// 命令列（測試用）：
//   /S                 不開視窗
//   /dir=<遊戲資料夾>   指定資料夾（預設自動從 Steam 找）
//   /uninstall         解除安裝
//   /log=<檔案>        把結果寫到檔案

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    public const string Version = "1.1";
    const string GameExe = "CONVRGENCE.exe";
    const string GameProcess = "CONVRGENCE";

    static readonly string[] OurFiles = {
        "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt",
        "toggle-chinese.bat", "Toggle-Chinese.ps1", "README-zh-TW.txt" };
    static readonly string[] OurDirs = { "BepInEx", "dotnet", "LICENSES" };

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = false, uninstall = false, auto = false;
        string dir = null, log = null;
        foreach (string a in args)
        {
            string l = a.ToLowerInvariant();
            if (l == "/s") silent = true;
            else if (l == "/uninstall") uninstall = true;
            else if (l == "/auto") auto = true;
            else if (l.StartsWith("/dir=")) dir = a.Substring(5).Trim('"');
            else if (l.StartsWith("/log=")) log = a.Substring(5).Trim('"');
        }

        if (silent)
        {
            if (dir == null) dir = FindGameFolder();
            string msg = "找不到遊戲資料夾";
            bool ok = dir != null && (uninstall ? Uninstall(dir, out msg) : Install(dir, out msg));
            if (log != null) File.WriteAllText(log, (ok ? "OK " : "FAIL ") + (dir ?? "") + "\r\n" + msg + "\r\n");
            return ok ? 0 : 1;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(dir ?? FindGameFolder(), auto, uninstall));
        return 0;
    }

    // ---------- 找遊戲資料夾 ----------

    public static string FindGameFolder()
    {
        var roots = new List<string>();
        AddRoot(roots, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
        AddRoot(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
        AddRoot(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string);

        var libs = new List<string>(roots);
        foreach (string root in roots)
        {
            string vdf = Path.Combine(root, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                AddRoot(libs, m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        foreach (string lib in libs)
        {
            string game = Path.Combine(lib, @"steamapps\common\CONVRGENCE");
            if (IsGameFolder(game)) return game;
        }
        return null;
    }

    static void AddRoot(List<string> list, string p)
    {
        if (string.IsNullOrEmpty(p)) return;
        p = p.Replace('/', '\\').TrimEnd('\\');
        foreach (string x in list)
            if (string.Equals(x, p, StringComparison.OrdinalIgnoreCase)) return;
        list.Add(p);
    }

    public static bool IsGameFolder(string dir)
    {
        return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, GameExe));
    }

    // ---------- 前置檢查 ----------

    static bool Precheck(string dir, out string msg)
    {
        if (!IsGameFolder(dir))
        {
            msg = "這個資料夾裡沒有 " + GameExe + "，請選擇 CONVRGENCE 的遊戲資料夾。";
            return false;
        }
        if (Process.GetProcessesByName(GameProcess).Length > 0)
        {
            msg = "遊戲正在執行中，請先完全關閉遊戲再操作。";
            return false;
        }
        msg = null;
        return true;
    }

    public static bool CanWrite(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".zh-tw-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    public static bool IsAdmin()
    {
        return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // ---------- 安裝 ----------

    public static bool Install(string dir, out string msg)
    {
        if (!Precheck(dir, out msg)) return false;
        try
        {
            // 之前用開關關掉中文時留下的舊翻譯，裝新版前先移除，避免開關腳本兩邊都有資料夾
            string offDir = Path.Combine(dir, @"BepInEx\Translation\zh-TW.off");
            if (Directory.Exists(offDir)) Directory.Delete(offDir, true);

            int count = 0;
            string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string dest = Path.GetFullPath(Path.Combine(dir, e.FullName.Replace('/', '\\')));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        continue; // 不允許寫到遊戲資料夾外面
                    if (e.FullName.EndsWith("/"))
                    {
                        Directory.CreateDirectory(dest);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    using (Stream src = e.Open())
                    using (FileStream dst = File.Create(dest))
                        src.CopyTo(dst);
                    count++;
                }
            }
            msg = "安裝完成，共 " + count + " 個檔案。\r\n\r\n" +
                  "遊戲語言請維持 English，照常從 Steam 啟動即可。\r\n" +
                  "第一次啟動會多等 10～30 秒，並跳出黑色視窗，屬正常現象。";
            return true;
        }
        catch (Exception ex)
        {
            msg = "安裝失敗：" + ex.Message;
            return false;
        }
    }

    // ---------- 解除安裝 ----------

    public static bool Uninstall(string dir, out string msg)
    {
        if (!Precheck(dir, out msg)) return false;
        try
        {
            int n = 0;
            foreach (string f in OurFiles)
            {
                string p = Path.Combine(dir, f);
                if (File.Exists(p)) { File.Delete(p); n++; }
            }
            foreach (string d in OurDirs)
            {
                string p = Path.Combine(dir, d);
                if (Directory.Exists(p)) { Directory.Delete(p, true); n++; }
            }
            msg = n == 0 ? "沒有找到已安裝的中文化，不需要移除。" : "已移除中文化，遊戲已回到原版。";
            return true;
        }
        catch (Exception ex)
        {
            msg = "移除失敗：" + ex.Message;
            return false;
        }
    }
}

class MainForm : Form
{
    readonly TextBox pathBox = new TextBox();
    readonly Label status = new Label();

    public MainForm(string dir, bool auto, bool autoUninstall)
    {
        Text = "CONVRGENCE 繁體中文化 v" + Program.Version;
        Font = new Font("Microsoft JhengHei UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 260);
        AutoScaleMode = AutoScaleMode.Dpi;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var title = new Label {
            Text = "CONVRGENCE 繁體中文化", Font = new Font("Microsoft JhengHei UI", 15f, FontStyle.Bold),
            AutoSize = true, Location = new Point(20, 16) };
        var sub = new Label {
            Text = "把遊戲的英文文字換成繁體中文。不修改遊戲本體，隨時可以移除。",
            AutoSize = true, Location = new Point(22, 54), ForeColor = Color.DimGray };
        var lbl = new Label { Text = "遊戲資料夾：", AutoSize = true, Location = new Point(22, 92) };
        pathBox.SetBounds(22, 116, 420, 28);
        pathBox.Text = dir ?? "";
        var browse = new Button { Text = "瀏覽…" };
        browse.SetBounds(450, 114, 88, 32);
        browse.Click += delegate { Browse(); };

        var install = new Button { Text = "安裝中文化" };
        install.SetBounds(22, 166, 160, 40);
        install.Click += delegate { Run(false); };
        var remove = new Button { Text = "解除安裝" };
        remove.SetBounds(192, 166, 120, 40);
        remove.Click += delegate { Run(true); };

        status.SetBounds(22, 216, 520, 40);
        status.ForeColor = Color.DimGray;
        status.Text = dir == null
            ? "沒有自動找到遊戲，請按「瀏覽…」選擇有 CONVRGENCE.exe 的資料夾。"
            : "已自動找到遊戲資料夾。";

        Controls.AddRange(new Control[] { title, sub, lbl, pathBox, browse, install, remove, status });
        AcceptButton = install;

        if (auto) Shown += delegate { Run(autoUninstall, true); };
    }

    void Browse()
    {
        using (var d = new FolderBrowserDialog())
        {
            d.Description = "選擇 CONVRGENCE 的遊戲資料夾（裡面有 CONVRGENCE.exe）";
            if (Directory.Exists(pathBox.Text)) d.SelectedPath = pathBox.Text;
            if (d.ShowDialog(this) == DialogResult.OK) pathBox.Text = d.SelectedPath;
        }
    }

    void Run(bool uninstall, bool confirmed = false)
    {
        string dir = pathBox.Text.Trim().Trim('"');
        if (!Program.IsGameFolder(dir))
        {
            MessageBox.Show(this, "這個資料夾裡沒有 CONVRGENCE.exe，請重新選擇。", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (uninstall && !confirmed && MessageBox.Show(this,
                "會刪除遊戲資料夾裡的 BepInEx、dotnet 等中文化檔案。\r\n" +
                "如果你另外裝了其他 BepInEx 模組，它們也會一起被移除。\r\n\r\n確定要解除安裝嗎？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        if (!Program.CanWrite(dir) && !Program.IsAdmin())
        {
            // 遊戲裝在 Program Files 之類需要權限的地方：用系統管理員身分重新執行
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath,
                    "/auto " + (uninstall ? "/uninstall " : "") + "\"/dir=" + dir + "\"") { Verb = "runas", UseShellExecute = true };
                Process.Start(psi);
                Close();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(this, "需要系統管理員權限才能寫入這個資料夾。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }

        UseWaitCursor = true;
        Enabled = false;
        string msg;
        bool ok = uninstall ? Program.Uninstall(dir, out msg) : Program.Install(dir, out msg);
        Enabled = true;
        UseWaitCursor = false;
        status.Text = ok ? (uninstall ? "已解除安裝。" : "安裝完成。") : "沒有完成，請看訊息。";
        MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }
}
