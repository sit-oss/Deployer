using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        public static DeployConfig Config { get; private set; } = null;

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

        public static async Task<bool> DoAction(string action, string target)
        {
            switch (action)
            {
                case "IfFileExist":
                    return File.Exists(target.ExtractEnvPath());
                case "IfFileInPath":
                    return CheckIfFileInPath(target);
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
                    var filePath = Path.Combine(DownloadPath,splDownload[2]);
                    if(File.Exists(filePath))
                    {
                        var md5 = File.ReadAllBytes(filePath).Md5();
                        if (md5 == splDownload[1])
                            return true;
                        File.Delete(filePath);
                    }
                    var downloadFile = await DownloadFile(splDownload[0], splDownload[1]);
                    if (downloadFile != null)
                    {
                        try
                        {
                            File.WriteAllBytes(filePath, downloadFile);
                            return true;
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }
                    break;
                case "DownloadFont":
                    var splDownloadFont = target.Split(new[] { '|' }, 3);
                    if (splDownloadFont.Length != 3) return false;
                    var windowsFontsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
                    var fontPath = Path.Combine(windowsFontsPath, Path.GetFileName(splDownloadFont[0]));
                    if(File.Exists(fontPath)) return true;
                    var downloadFont = await DownloadFile(splDownloadFont[0], splDownloadFont[1]);
                    if (downloadFont != null)
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
                    var filePathAdmin = Path.Combine(DownloadPath, splRunAdmin[0]);
                    if (File.Exists(filePathAdmin))
                        return await RunProcess(filePathAdmin, splRunAdmin[1].ExtractEnvPath(),true) == 0;
                    break;
                case "RunAsUser":
                    var splRunUser = target.Split(new[] { '|' }, 2);
                    if (splRunUser.Length != 2) return false;
                    var filePathUser = Path.Combine(DownloadPath, splRunUser[0]);
                    if (File.Exists(filePathUser))
                        return await RunProcess(filePathUser, splRunUser[1].ExtractEnvPath()) == 0;
                    break;
                case "RunAtNewDesktop":
                    var splRunDesktop = target.Split(new[] { '|' }, 2);
                    if (splRunDesktop.Length != 2) return false;
                    var filePathDesktop = Path.Combine(DownloadPath, splRunDesktop[0]);
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
                        var runCommand = await RunProcess(splCmd[0].ExtractEnvPath(), splCmd[1]);
                        if(runCommand == 0) runCommandCount++;
                    }
                    if (runCommandCount == splRunCommands.Length)
                        return true;
                    break;
                case "PerformMsiexec":
                    var splMsi = target.Split(new[] { '|' }, 2);
                    if (splMsi.Length != 2) return false;
                    var filePathMsi = Path.Combine(DownloadPath, splMsi[0]);
                    if (File.Exists(filePathMsi))
                        return await RunMsiexec($"/i {filePathMsi} {splMsi[1].ExtractEnvPath()}") == 0;
                    break;
                case "WriteToFile":
                    var splWrite = target.Split(new[] { '|' }, 2);
                    if (splWrite.Length != 2) return false;
                    var filePathWrite = splWrite[0].ExtractEnvPath();
                    var content = Encoding.UTF8.GetString(Convert.FromBase64String(splWrite[1]));
                    if (string.IsNullOrEmpty(filePathWrite) || string.IsNullOrEmpty(content)) return false;
                    try
                    {
                        var dirPath = Path.GetDirectoryName(filePathWrite);
                        if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
                            Directory.CreateDirectory(dirPath);
                        File.WriteAllText(filePathWrite,content);
                        return true;
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                    break;
                case "ExtractZipFile":
                    var splZip = target.Split(new[] { '|' }, 2);
                    if (splZip.Length != 2) return false;
                    var filePathZip = Path.Combine(DownloadPath, splZip[0]);
                    try
                    {
                        ZipFile.ExtractToDirectory(filePathZip, splZip[1].ExtractEnvPath());
                        return true;
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                    break;
                case "AddToEnv":
                    var splEnv = target.Split(new[] { '|' }, 3);
                    if (splEnv.Length != 3) return false;
                    var envScope = Enum.TryParse(splEnv[0], out EnvironmentVariableTarget envTarget) ? envTarget : EnvironmentVariableTarget.User;
                    var envName = splEnv[1];
                    var envPath = splEnv[2].ExtractEnvPath();
                    var oldEnvValue = Environment.GetEnvironmentVariable(envName, envScope);
                    var newValue = oldEnvValue + $";{envPath}";
                    try
                    {
                        Environment.SetEnvironmentVariable(envName, newValue, envScope);
                        return true;
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                    break;
                case "WaitProcess":
                    for (var i = 0; i < 20; i++)
                    {
                        if(Process.GetProcessesByName(target).Any())
                            return true;
                        await Task.Delay(500);
                    }
                    break;
                case "KillProcess":
                    var processesForKill = Process.GetProcessesByName(target);
                    foreach (var process in processesForKill)
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit();
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }
                    break;
                case "RemoveFile":
                    var filePathForRemove = Path.Combine(DownloadPath, target);
                    if (File.Exists(filePathForRemove))
                    {
                        try
                        {
                            File.Delete(filePathForRemove);
                            return true;
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }
                    break;

            }
            return false;
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

        public static async Task<int> RunProcess(string path, string param,bool runas = false)
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
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                }
            };
            if (runas) process.StartInfo.Verb = "runas";
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode;
        }

        public static async Task<int> RunMsiexec(string args)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "msiexec",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
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

        public static async Task<byte[]> DownloadFile(string url,string md5, int timeout = 30)
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(timeout);
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsByteArrayAsync();
                    if (result.Md5() == md5)
                        return result;
                }
                return null;
            }
        }

        public static bool CheckIfFileInPath(string fileName)
        {
            var find = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)?.Split(';').Select(s => Path.Combine(s, "gcc.exe")).FirstOrDefault(File.Exists) ?? Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)?.Split(';').Select(s => Path.Combine(s, "gcc.exe")).FirstOrDefault(File.Exists);
            return find != null;
        }

        // private const int MAX_PATH = 260;
        // [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = false)]
        // static extern bool PathFindOnPath([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] StringBuilder pszFile, [System.Runtime.InteropServices.In] string[] ppszOtherDirs);
        // public static string GetFullPathFromWindows(string exeName)
        // {
        //     if (exeName.Length >= MAX_PATH)
        //         return null;
        //     var sb = new StringBuilder(exeName, MAX_PATH);
        //     return PathFindOnPath(sb, null) ? sb.ToString() : null;
        // }

        #region Ext Functions

        public static string Md5(this byte[] content, Encoding encoding = null)
        {
            using (var sha = MD5.Create())
            {
                return sha.ComputeHash(content).Aggregate("", (current, b) => current + b.ToString("x2"));
            }
        }

        public static Task WaitForExitAsync(this Process process, 
            CancellationToken cancellationToken = default(CancellationToken))
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
        public static string ExtractEnvPath(this string path)
        {
            if(string.IsNullOrEmpty(path) || !path.Contains('%') || !rgxEnvs.IsMatch(path)) return path;
            foreach (System.Text.RegularExpressions.Match match in rgxEnvs.Matches(path))
            {
                if (!Enum.TryParse(match.Value.Replace("%", ""), out Environment.SpecialFolder folder)) continue;
                var gp = Environment.GetFolderPath(folder);
                return path.Replace(match.Value, gp);
            }
            return path;
        }
        #endregion
    }
}
