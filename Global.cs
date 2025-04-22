using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Deployer
{
    internal static class Global
    {
        public static string DownloadPath { get; private set; }
        public static DeployConfig Config { get; private set; }
        public static async Task<bool> DoAction(string action, string target)
        {
            switch (action)
            {
                case "IfFileExist":
                    return File.Exists(target.ExtractEnvPath());
                case "IfFileInEnv":
                    return CheckIfFileInEnvironment(target);
                case "Delay":
                    if (int.TryParse(target, out var delay))
                    {
                        await Task.Delay(delay);
                        return true;
                    }
                    break;
                case "DownloadFile":
                    var splDownload = target.Split(new[] { '|' }, 3);
                    if (splDownload.Length != 3) return false;
                    var filePath = splDownload[2].ExtractEnvPath();
                    if(File.Exists(filePath))
                    {
                        var md5 = File.ReadAllBytes(filePath).Md5();
                        if (md5 == splDownload[1])
                            return true;
                        File.Delete(filePath);
                    }
                    var downloadFile = await DownloadFile(splDownload[0]);
                    if (downloadFile != null && downloadFile.Md5() == splDownload[1])
                    {
                        File.WriteAllBytes(filePath, downloadFile);
                        return true;
                    }
                    break;
                case "DownloadFont":
                    var splDownloadFont = target.Split(new[] { '|' }, 3);
                    if (splDownloadFont.Length != 3) return false;
                    var windowsFontsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
                    if (!Directory.Exists(windowsFontsPath)) Directory.CreateDirectory(windowsFontsPath);
                    var fontPath = Path.Combine(windowsFontsPath, Path.GetFileName(splDownloadFont[0]));
                    if(File.Exists(fontPath)) return true;
                    var downloadFont = await DownloadFile(splDownloadFont[0]);
                    if (downloadFont != null && downloadFont.Md5() == splDownloadFont[1])
                    {
                        File.WriteAllBytes(fontPath, downloadFont);
                        var fontKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts");
                        fontKey?.SetValue($"{splDownloadFont[2]} (TrueType)", fontPath);
                        fontKey?.Close();
                        return true;
                    }
                    break;
                case "RunAsAdmin":
                    var splRunAdmin = target.Split(new[] { '|' }, 2);
                    if (splRunAdmin.Length != 2) return false;
                    var filePathAdmin = splRunAdmin[0].ExtractEnvPath();
                    if (File.Exists(filePathAdmin))
                        return await RunProcess(filePathAdmin, splRunAdmin[1].ExtractEnvPath(),true,true) == 0;
                    break;
                case "RunAsUser":
                    var splRunUser = target.Split(new[] { '|' }, 2);
                    if (splRunUser.Length != 2) return false;
                    var filePathUser = splRunUser[0].ExtractEnvPath();
                    if (File.Exists(filePathUser))
                        return await RunProcess(filePathUser, splRunUser[1].ExtractEnvPath(), false, true) == 0;
                    break;
                case "RunAtNewDesktop":
                    var splRunDesktop = target.Split(new[] { '|' }, 2);
                    if (splRunDesktop.Length != 2) return false;
                    var filePathDesktop = splRunDesktop[0].ExtractEnvPath();
                    if (File.Exists(filePathDesktop))
                        return await RunAtNewDesktop($"{filePathDesktop} {splRunDesktop[1].ExtractEnvPath()}") == 0;
                    break;
                case "RunCommands":
                    var splRunCommands = target.Split('|');
                    var runCommandCount = 0;
                    foreach (var splRunCommand in splRunCommands)
                    {
                        var splCmd = splRunCommand.Split('?');
                        if (splCmd.Length != 2) return false;
                        var runCommand = await RunProcess(splCmd[0].ExtractEnvPath(), splCmd[1].ExtractEnvPath(), false, true);
                        if(runCommand == 0) runCommandCount++;
                    }
                    if (runCommandCount == splRunCommands.Length)
                        return true;
                    break;
                case "RunCommandsAdmin":
                    var splRunCommandsAdmin = target.Split('|');
                    var runCommandAdminCount = 0;
                    foreach (var splRunCommand in splRunCommandsAdmin)
                    {
                        var splCmd = splRunCommand.Split('?');
                        if (splCmd.Length != 2) return false;
                        var runCommand = await RunProcess(splCmd[0].ExtractEnvPath(), splCmd[1].ExtractEnvPath(), true, true);
                        if (runCommand == 0) runCommandAdminCount++;
                    }
                    if (runCommandAdminCount == splRunCommandsAdmin.Length)
                        return true;
                    break;
                case "RunAndWait":
                    var splRun = target.Split(new[] { '|' }, 3);
                    var runPath = splRun[0].ExtractEnvPath();
                    if (File.Exists(runPath) && int.TryParse(splRun[2],out var runTimeOut))
                    {
                        await RunAndExit(runPath, splRun[1].ExtractEnvPath(), runTimeOut);
                        return true;
                    }
                    break;
                case "PerformMsiexec":
                    var filePathMsi = target.ExtractEnvPath();
                    if (File.Exists(filePathMsi))
                        return await RunMsiexec($"{filePathMsi}") == 0;
                    break;
                case "WriteToFile":
                    var splWrite = target.Split(new[] { '|' }, 2);
                    if (splWrite.Length != 2) return false;
                    var filePathWrite = splWrite[0].ExtractEnvPath();
                    var content = Encoding.UTF8.GetString(Convert.FromBase64String(splWrite[1]));
                    if (string.IsNullOrEmpty(filePathWrite) || string.IsNullOrEmpty(content)) return false;
                    return WriteToFile(filePathWrite, content);
                case "ExtractZipFile":
                    var splZip = target.Split(new[] { '|' }, 2);
                    if (splZip.Length != 2) return false;
                    return await ExtractZipFile(splZip[0].ExtractEnvPath(), splZip[1].ExtractEnvPath());
                case "AddToEnv":
                    var splEnv = target.Split(new[] { '|' }, 3);
                    return splEnv.Length == 3 && AddEnvironment(splEnv[1], splEnv[2], splEnv[0]);
                case "WaitProcess":
                    return await WaitProcess(target);
                case "KillProcess":
                    return await KillProcess(target);
                case "RemoveFile":
                    return RemoveFile(target.ExtractEnvPath());
                case "RemoveDir":
                    return RemoveFolder(target.ExtractEnvPath());
            }
            return false;
        }

        #region Actions
        private static bool WriteToFile(string path, string content)
        {
            try
            {
                var dirPath = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
                    Directory.CreateDirectory(dirPath);
                File.WriteAllText(path,content);
                return true;
            }
            catch (Exception)
            {
                // ignored
            }
            return false;
        }
        private static async Task<bool> ExtractZipFile(string source, string dist)
        {
            try
            {
                await Task.Factory.StartNew(() => { ZipFile.ExtractToDirectory(source, dist); });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private static bool AddEnvironment(string name,string value,string scope)
        {
            try
            {
                if (!Enum.TryParse(scope, out EnvironmentVariableTarget envScope)) return false;
                var oldValue = Environment.GetEnvironmentVariable(name, envScope);
                Environment.SetEnvironmentVariable(name, string.IsNullOrEmpty(oldValue) ? value : $"{oldValue};{value}", envScope);
                return true;
            }
            catch (Exception)
            {
                // ignored
            }
            return false;
        }
        private static async Task<bool> WaitProcess(string processName)
        {
            for (var i = 0; i < 20; i++)
            {
                if (Process.GetProcessesByName(processName).Any())
                    return true;
                await Task.Delay(500);
            }
            return false;
        }
        private static async Task<bool> KillProcess(string processName)
        {
            var processes = Process.GetProcessesByName(processName);
            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    await process.WaitForExitAsync();
                }
                catch (Exception)
                {
                    // ignored
                }
            }
            return true;
        }
        private static bool RemoveFile(string path)
        {
            if (!File.Exists(path)) return true;
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                // ignored
            }
            return false;
        }
        private static bool RemoveFolder(string path)
        {
            if (!Directory.Exists(path)) return true;
            try
            {
                Directory.Delete(path, true);
                return true;
            }
            catch (Exception)
            {
                // ignored
            }
            return false;
        }
        #endregion

        #region Funcs
        public static async Task LoadConfigs()
        {
            DownloadPath = Path.Combine(Path.GetTempPath(), "SITDeployer");
            if (!Directory.Exists(DownloadPath))
                Directory.CreateDirectory(DownloadPath);

            string result = null;
#if DEBUG
            const string debugFileName = "config.json";
            if (File.Exists(debugFileName))
            {
                result = File.ReadAllText(debugFileName);
                Config = !string.IsNullOrEmpty(result) ? SimpleJson.SimpleJson.DeserializeObject<DeployConfig>(result) : null;
                return;
            }
#endif
            var fromEnvironmentVariable = Environment.GetEnvironmentVariable("DEPLOYER_CONFIG_URL");
            if (!string.IsNullOrEmpty(fromEnvironmentVariable))
                result = await HttpGet(fromEnvironmentVariable, 5000);
            if (string.IsNullOrEmpty(result) && await Ping("10.15.32.82"))
                result = await HttpGet("http://10.15.32.82/deployer/config.json?t=" + Guid.NewGuid().ToString("N"), 1000);
            if (string.IsNullOrEmpty(result))
                result = await HttpGet("https://pub.shonan-it.ac/deployer/config.json?t=" + Guid.NewGuid().ToString("N"), 3000);
            Config = !string.IsNullOrEmpty(result) ? SimpleJson.SimpleJson.DeserializeObject<DeployConfig>(result) : null;
        }
        public static async Task<string> HttpGet(string url, int timeout)
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                client.Timeout = TimeSpan.FromMilliseconds(timeout);
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return null;
            }
        }
        public static async Task<int> RunAtNewDesktop(string command)
        {
            using (var desktop = Onyeyiri.Desktop.CreateDesktop("sitdeployer"))
            {
                var p = desktop.CreateProcess(command);
                await p.WaitForExitAsync();
                desktop.Close();
                return p.ExitCode;
            }
        }
        public static async Task<int> RunProcess(string path, string param, bool runas = false, bool hide = false)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = param,
                    UseShellExecute = !hide,
                    RedirectStandardOutput = hide,
                    RedirectStandardError = hide,
                    CreateNoWindow = hide
                }
            };
            if (hide) process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            if (runas) process.StartInfo.Verb = "runas";
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        public static async Task RunAndExit(string path, string param, int timeout)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = param,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            process.Start();
            await Task.Delay(timeout);
            process.Kill();
            process.WaitForExit();
        }
        public static async Task<int> RunMsiexec(string args)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i {args} /passive /qn /norestart",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                }
            };
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        public static async Task<bool> Ping(string ip)
        {
            try { 
                var myPing = new System.Net.NetworkInformation.Ping();
                var buffer = new byte[32];
                var pingOptions = new System.Net.NetworkInformation.PingOptions();
                var reply = await myPing.SendPingAsync(ip, 500, buffer, pingOptions);
                return reply?.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch (Exception) {
                return false;
            }
        }
        public static async Task<byte[]> DownloadFile(string url,int timeout = 30000)
        {
            using (var client = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = true }))
            {
                client.Timeout = TimeSpan.FromMilliseconds(timeout);
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                var result = await response.Content.ReadAsByteArrayAsync();
                if (result != null && result.Length > 0)
                    return result;
            }
            return null;
        }
        public static async Task<byte[]> DownloadFile(string url, IProgress<double> progress)
        {
            var handler = new System.Net.Http.HttpClientHandler { AllowAutoRedirect = true };
            using (var client = new System.Net.Http.HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromMinutes(3);
                var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"The request returned with HTTP status code {response.StatusCode}");
                }
                var total = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = total != -1 && progress != null;
                using (var ms = new MemoryStream())
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var totalRead = 0L;
                    var buffer = new byte[4096];
                    var isMoreToRead = true;
                    do
                    {
                        var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (read == 0)
                        {
                            isMoreToRead = false;
                        }
                        else
                        {
                            var data = new byte[read];
                            buffer.ToList().CopyTo(0, data, 0, read);
                            await ms.WriteAsync(data, 0, data.Length);
                            totalRead += read;
                            if (canReportProgress)
                                progress.Report(totalRead * 1d / (total * 1d) * 100);
                        }
                    } while (isMoreToRead);
                    return ms.ToArray();
                }
            }
        }
        public static bool CheckIfFileInEnvironment(string fileName)
        {
            var find = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)?.Split(';').Select(s => Path.Combine(s, "gcc.exe")).FirstOrDefault(File.Exists) ?? Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)?.Split(';').Select(s => Path.Combine(s, "gcc.exe")).FirstOrDefault(File.Exists);
            return find != null;
        }
        #endregion

        #region Ext Functions

        public static string Md5(this byte[] content, Encoding encoding = null)
        {
            using (var sha = MD5.Create())
            {
                return sha.ComputeHash(content).Aggregate("", (current, b) => current + b.ToString("x2"));
            }
        }

        public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (process.HasExited) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(null);
            if(cancellationToken != CancellationToken.None)
                cancellationToken.Register(() => tcs.SetCanceled());

            return process.HasExited ? Task.CompletedTask : tcs.Task;
        }

        private static readonly System.Text.RegularExpressions.Regex rgxEnvs = new System.Text.RegularExpressions.Regex(@"%(\w+)%", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        public static string ExtractEnvPath(this string path,bool reverseSlash = false)
        {
            var result = path;
            if (!string.IsNullOrEmpty(result) && result.Contains('%') && rgxEnvs.IsMatch(result))
            {
                foreach (System.Text.RegularExpressions.Match match in rgxEnvs.Matches(result))
                {
                    switch (match.Value)
                    {
                        case "%DP%":
                        case "%DownloadPath%":
                            result = result.Replace(match.Value, DownloadPath);
                            continue;
                        case "%UserName%":
                            result = result.Replace(match.Value, Environment.UserName);
                            continue;
                    }
                    if (!Enum.TryParse(match.Value.Replace("%", ""), out Environment.SpecialFolder folder)) continue;
                    result = result.Replace(match.Value, Environment.GetFolderPath(folder));
                }
            }
            if(reverseSlash)
                result = result?.Replace("\\", "/");
            return result;
        }
        #endregion
    }
}
