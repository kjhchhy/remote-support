using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("원격지원 설치 도우미")]
[assembly: AssemblyDescription("RustDesk 공식 최신 정식 버전 설치 및 서버 주소 설정")]
[assembly: AssemblyCompany("kjhchhy")]
[assembly: AssemblyProduct("Remote Support Setup")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace RemoteSupport {
    public class ReleaseAsset {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public string digest { get; set; }
        public long size { get; set; }
    }
    public class Release {
        public string tag_name { get; set; }
        public bool prerelease { get; set; }
        public bool draft { get; set; }
        public ReleaseAsset[] assets { get; set; }
    }
    public class TimedClient : WebClient {
        protected override WebRequest GetWebRequest(Uri address) {
            var request = base.GetWebRequest(address);
            request.Timeout = 60000;
            var http = request as HttpWebRequest;
            if (http != null) http.ReadWriteTimeout = 60000;
            return request;
        }
    }
    public static class SetupLogic {
        public const string Server = "ds307.duckdns.org";
        public const string LatestUrl = "https://api.github.com/repos/rustdesk/rustdesk/releases/latest";
        public static string ConfigString() {
            return ConfigString(Server, Server);
        }
        public static string ConfigString(string host, string relay) {
            var value = new Dictionary<string, string> {
                {"host", host}, {"relay", relay}, {"key", ""}, {"api", ""}
            };
            string json = new JavaScriptSerializer().Serialize(value);
            char[] encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_').ToCharArray();
            Array.Reverse(encoded);
            return new string(encoded);
        }
        public static string AssetSuffix(string architecture, bool is64) {
            if (string.Equals(architecture, "ARM64", StringComparison.OrdinalIgnoreCase)) return "-aarch64.exe";
            if (is64) return "-x86_64.exe";
            throw new InvalidOperationException("이 설치 도우미는 Windows 10/11의 64비트 PC용입니다.");
        }
        public static ReleaseAsset SelectAsset(Release release, string suffix) {
            if (release == null || release.draft || release.prerelease || release.assets == null ||
                string.IsNullOrEmpty(release.tag_name)) throw new InvalidDataException("최신 정식 버전 정보를 확인할 수 없습니다.");
            ReleaseAsset selected = null;
            foreach (var asset in release.assets) {
                if (asset.name != null && asset.name.StartsWith("rustdesk-", StringComparison.Ordinal) && asset.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                    if (selected != null) throw new InvalidDataException("설치 파일이 여러 개라 자동 선택을 중단했습니다.");
                    selected = asset;
                }
            }
            if (selected == null) throw new InvalidDataException("이 PC에 맞는 최신 설치 파일이 없습니다.");
            Uri uri;
            if (!Uri.TryCreate(selected.browser_download_url, UriKind.Absolute, out uri) || uri.Scheme != "https" ||
                uri.Host != "github.com" || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) ||
                !uri.AbsolutePath.StartsWith("/rustdesk/rustdesk/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("공식 RustDesk 다운로드 주소가 아닙니다.");
            if (selected.size < 1024 || selected.size > 1024L * 1024 * 1024 ||
                selected.digest == null || !Regex.IsMatch(selected.digest, @"\Asha256:[a-fA-F0-9]{64}\z"))
                throw new InvalidDataException("공식 파일 검증 정보를 확인할 수 없어 설치를 중단했습니다.");
            return selected;
        }
        public static string Hash(string file) {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static void Verify(string file, ReleaseAsset asset) {
            if (new FileInfo(file).Length != asset.size || !string.Equals("sha256:" + Hash(file), asset.digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("다운로드 파일 검증에 실패했습니다. 다시 실행해 주세요.");
        }
        public static Version ParseVersion(string value) {
            var match = Regex.Match(value ?? "", @"\d+\.\d+\.\d+(?:\.\d+)?");
            if (!match.Success) throw new InvalidDataException("버전 정보를 읽을 수 없습니다.");
            var v = new Version(match.Value);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }
        public static string InstalledExe() {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RustDesk")) {
                    if (key == null) continue;
                    string dir = key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(dir)) {
                        string exe = Path.Combine(dir, "rustdesk.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
            }
            return null;
        }
        public static bool IsCurrent(string exe, Version expected) {
            if (string.IsNullOrEmpty(exe)) return false;
            try { return ParseVersion(FileVersionInfo.GetVersionInfo(exe).FileVersion) >= expected; }
            catch { return false; }
        }
        public static string RunCommand(string exe, string args, int timeoutSeconds, bool capture) {
            var info = new ProcessStartInfo(exe, args) {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe),
                RedirectStandardOutput = capture, RedirectStandardError = capture
            };
            using (var process = Process.Start(info)) {
                Task<string> output = capture ? process.StandardOutput.ReadToEndAsync() : null;
                Task<string> error = capture ? process.StandardError.ReadToEndAsync() : null;
                if (!process.WaitForExit(timeoutSeconds * 1000)) throw new System.TimeoutException("RustDesk 작업이 예상보다 오래 걸립니다. 잠시 후 다시 실행해 주세요.");
                if (process.ExitCode != 0) throw new InvalidOperationException("RustDesk 작업이 실패했습니다. 오류 코드: " + process.ExitCode);
                if (!capture) return "";
                if (!Task.WaitAll(new Task[] { output, error }, 5000)) throw new System.TimeoutException("RustDesk 설정 확인 응답이 지연되고 있습니다.");
                return output.Result.Trim();
            }
        }
        public static void EnsureService() {
            // The portable installer can exit before its unpacked child finishes installation.
            DateTime deadline = DateTime.UtcNow.AddSeconds(90);
            Exception lastError = null;
            do {
                try {
                    using (var service = new ServiceController("RustDesk")) {
                        service.Refresh();
                        if (service.Status == ServiceControllerStatus.Running) return;
                        if (service.Status == ServiceControllerStatus.Stopped) service.Start();
                    }
                } catch (InvalidOperationException error) { lastError = error; }
                  catch (System.ComponentModel.Win32Exception error) { lastError = error; }
                Thread.Sleep(1500);
            } while (DateTime.UtcNow < deadline);
            throw new InvalidOperationException("RustDesk 서비스가 준비되지 않았습니다. 잠시 후 다시 실행해 주세요.", lastError);
        }
        public static bool ConfigMatches(Func<string, string> readOption, string host, string relay) {
            return readOption("custom-rendezvous-server") == host && readOption("relay-server") == relay &&
                readOption("key") == "" && readOption("api-server") == "";
        }
        public static void ApplyServerConfig(string exe, string host, string relay, Action<string> log) {
            EnsureService();
            for (int attempt = 1; attempt <= 3; attempt++) {
                log("Applying server configuration; attempt " + attempt);
                RunCommand(exe, "--config " + ConfigString(host, relay), 45, false);
                Thread.Sleep(1500);
                if (ConfigMatches(delegate(string option) { return RunCommand(exe, "--option " + option, 15, true); }, host, relay)) {
                    log("Server configuration read-back verified; ID and relay match; key and API are empty");
                    return;
                }
                Thread.Sleep(1000);
            }
            throw new InvalidOperationException("설치는 완료됐지만 서버 설정을 확인하지 못했습니다. 도우미를 다시 실행해 주세요.");
        }
        public static TimedClient Client() {
            var client = new TimedClient();
            client.Headers[HttpRequestHeader.UserAgent] = "kjhchhy-RemoteSupportSetup/1.0";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }
    }
    public class SetupForm : Form {
        readonly Label title = new Label();
        readonly Label status = new Label();
        readonly Label detail = new Label();
        readonly ProgressBar progress = new ProgressBar();
        readonly Button finish = new Button();
        bool busy = true;
        string logFile;
        string installedExe;
        public SetupForm() {
            Text = "원격지원 설치 · 1.0.1";
            ClientSize = new Size(540, 325);
            Font = new Font("맑은 고딕", 10);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            title.SetBounds(28, 26, 484, 42);
            title.Font = new Font("맑은 고딕", 20, FontStyle.Bold);
            title.Text = "원격지원을 준비하고 있어요";
            status.SetBounds(30, 88, 480, 48);
            status.Text = "최신 RustDesk 버전을 확인합니다…";
            progress.SetBounds(30, 145, 480, 10);
            progress.Style = ProgressBarStyle.Marquee;
            detail.SetBounds(30, 174, 480, 73);
            detail.ForeColor = Color.FromArgb(75, 85, 99);
            detail.Text = "인터넷 연결을 유지해 주세요.\n설치 후 서버 주소가 자동으로 입력됩니다.\n기존 RustDesk를 업데이트하면 원격 연결이 잠시 끊길 수 있습니다.";
            finish.SetBounds(345, 262, 165, 38);
            finish.Text = "설치 중";
            finish.Enabled = false;
            finish.Click += delegate { if (!string.IsNullOrEmpty(installedExe)) OpenRustDesk(); Close(); };
            Controls.AddRange(new Control[] { title, status, progress, detail, finish });
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            Shown += async delegate { await Install(); };
        }
        void Report(string text, int percent) {
            if (InvokeRequired) { BeginInvoke(new Action<string, int>(Report), text, percent); return; }
            status.Text = text;
            if (percent >= 0) { progress.Style = ProgressBarStyle.Continuous; progress.Value = Math.Min(100, percent); }
            else progress.Style = ProgressBarStyle.Marquee;
        }
        void Log(string text) {
            if (logFile != null) File.AppendAllText(logFile, DateTime.UtcNow.ToString("o") + " " + text + Environment.NewLine, Encoding.UTF8);
        }
        static string SecureWorkDir() {
            string path = Path.Combine(Path.GetTempPath(), "RemoteSupportSetup-" + Guid.NewGuid().ToString("N"));
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(path, security);
            return path;
        }
        void Run(string exe, string args, int timeoutSeconds) {
            SetupLogic.RunCommand(exe, args, timeoutSeconds, false);
        }
        void OpenRustDesk() {
            try { Process.Start(new ProcessStartInfo(installedExe) { UseShellExecute = true }); }
            catch { MessageBox.Show("바탕화면의 RustDesk 아이콘을 눌러 실행해 주세요.", "설치 완료"); }
        }
        async Task Install() {
            try {
                if (!Environment.Is64BitOperatingSystem || Environment.OSVersion.Version.Major < 10)
                    throw new InvalidOperationException("Windows 10 또는 11의 64비트 PC에서 실행해 주세요.");
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string work = SecureWorkDir();
                logFile = Path.Combine(work, "setup.log");
                Log("Setup version " + Assembly.GetExecutingAssembly().GetName().Version);
                string json;
                using (var client = SetupLogic.Client()) json = await client.DownloadStringTaskAsync(SetupLogic.LatestUrl);
                var release = new JavaScriptSerializer().Deserialize<Release>(json);
                string arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
                var asset = SetupLogic.SelectAsset(release, SetupLogic.AssetSuffix(arch, Environment.Is64BitOperatingSystem));
                Version expected = SetupLogic.ParseVersion(release.tag_name);
                Log("Release " + release.tag_name + "; asset " + asset.name);
                installedExe = SetupLogic.InstalledExe();
                if (!SetupLogic.IsCurrent(installedExe, expected)) {
                    string download = Path.Combine(work, "rustdesk-installer.exe");
                    Report("RustDesk " + release.tag_name + " 다운로드 중…", 0);
                    using (var client = SetupLogic.Client()) {
                        client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e) { Report("RustDesk " + release.tag_name + " 다운로드 중… " + e.ProgressPercentage + "%", e.ProgressPercentage); };
                        await client.DownloadFileTaskAsync(new Uri(asset.browser_download_url), download);
                    }
                    Report("다운로드 파일을 검증하고 있습니다…", -1);
                    await Task.Run(delegate { SetupLogic.Verify(download, asset); });
                    Log("SHA-256 verified");
                    Report("RustDesk를 설치하고 있습니다…", -1);
                    await Task.Run(delegate {
                        Run(download, "--silent-install", 240);
                        var deadline = DateTime.UtcNow.AddSeconds(90);
                        do {
                            installedExe = SetupLogic.InstalledExe();
                            if (SetupLogic.IsCurrent(installedExe, expected)) break;
                            Thread.Sleep(1500);
                        } while (DateTime.UtcNow < deadline);
                        if (!SetupLogic.IsCurrent(installedExe, expected)) throw new InvalidOperationException("설치된 버전을 확인할 수 없습니다. 잠시 후 다시 실행해 주세요.");
                        // The download is a self-extracting package, not the installed executable.
                        // Its SHA-256 is verified before execution; validate the installed version and service here.
                        SetupLogic.EnsureService();
                        Log("Installed version verified: " + FileVersionInfo.GetVersionInfo(installedExe).FileVersion);
                    });
                    try { File.Delete(download); } catch { }
                } else Log("Current or newer version already installed; no downgrade");
                Report("원격지원 서버를 설정하고 있습니다…", -1);
                await Task.Run(delegate {
                    SetupLogic.ApplyServerConfig(installedExe, SetupLogic.Server, SetupLogic.Server, Log);
                });
                Report("설치와 서버 설정 확인이 완료되었습니다.", 100);
                title.Text = "이제 RustDesk를 열어 주세요";
                detail.Text = "RustDesk에서 ‘준비 완료’가 표시되는지 확인한 뒤\n‘내 데스크탑’의 ID를 지원 담당자에게 알려 주세요.\n연결 요청이 오면 직접 확인하고 승인해 주세요.";
                finish.Text = "RustDesk 열기";
                busy = false;
                finish.Enabled = true;
            } catch (Exception error) {
                try { Log(error.ToString()); } catch { }
                installedExe = null;
                Report("설치를 완료하지 못했습니다.", 0);
                title.Text = "확인이 필요해요";
                detail.Text = error is WebException ? "다운로드 서버에 연결하지 못했습니다.\n인터넷 연결을 확인하고 잠시 후 다시 실행해 주세요." : error.Message;
                finish.Text = "닫기";
                busy = false;
                finish.Enabled = true;
                if (logFile != null) {
                    var log = new LinkLabel { Text = "오류 기록 보기", AutoSize = true, Location = new Point(30, 273) };
                    log.LinkClicked += delegate { Process.Start("notepad.exe", "\"" + logFile + "\""); };
                    Controls.Add(log);
                }
            }
        }
    }
    static class Program {
        [STAThread]
        static void Main() {
            bool first;
            using (var mutex = new Mutex(true, @"Local\KjhchhyRemoteSupportSetup", out first)) {
                if (!first) { MessageBox.Show("설치 도우미가 이미 실행 중입니다."); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SetupForm());
            }
        }
    }
}
