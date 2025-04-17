using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            const string debugFileName = "Y:\\config.json";
            if (File.Exists(debugFileName))
            {
                result = File.ReadAllText(debugFileName);
                Config = !string.IsNullOrEmpty(result) ? SimpleJson.SimpleJson.DeserializeObject<DeployConfig>(result) : null;
                return;
            }
#endif
            var fromEnvironmentVariable = Environment.GetEnvironmentVariable("DEPLOYER_CONFIG_URL");
            if (!string.IsNullOrEmpty(fromEnvironmentVariable))
                result = await HttpGet(fromEnvironmentVariable);
            if (string.IsNullOrEmpty(result))
                result = await HttpGet("http://10.15.32.82/deployer/config.json?t="+Guid.NewGuid().ToString("N"));
            if (string.IsNullOrEmpty(result))
                result = await HttpGet("https://pub.shonan-it.ac/deployer/config.json?t="+Guid.NewGuid().ToString("N"));
            Config = !string.IsNullOrEmpty(result) ? SimpleJson.SimpleJson.DeserializeObject<DeployConfig>(result) : null;
        }

        public static async Task<string> HttpGet(string url, int timeout = 2000)
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
                    return File.Exists(target);
                case "DownloadFile":
                    var spl = target.Split(new[] { '|' }, 3);
                    if (spl.Length != 3) return false;
                    var filePath = Path.Combine(DownloadPath,spl[2]);
                    if(File.Exists(filePath))
                    {
                        var md5 = File.ReadAllBytes(filePath).md5();
                        if (md5 == spl[1])
                            return true;
                        File.Delete(filePath);
                    }
                    using (var client = new System.Net.Http.HttpClient())
                    {
                        var response = await client.GetAsync(spl[0]);
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsByteArrayAsync();
                            var md5 = content.md5();
                            if(md5 != spl[1])
                                return false;
                            File.WriteAllBytes(filePath, content);
                            return true;
                        }
                    }
                    break;
                case "RunAsAdmin":
                    var splRunAdmin = target.Split(new[] { '|' }, 2);
                    if (splRunAdmin.Length != 2) return false;
                    var filePathAdmin = Path.Combine(DownloadPath, splRunAdmin[0]);
                    if (File.Exists(filePathAdmin))
                        return await RunProcessAsAdmin(filePathAdmin, splRunAdmin[1]) == 0;
                    break;
                case "RunAsUser":
                    var splRunUser = target.Split(new[] { '|' }, 2);
                    if (splRunUser.Length != 2) return false;
                    var filePathUser = Path.Combine(DownloadPath, splRunUser[0]);
                    if (File.Exists(filePathUser))
                        return await RunProcessAsUser(filePathUser, splRunUser[1]) == 0;
                    break;

            }
            return false;
        }

        public static async Task<int> RunProcessAsUser(string path, string param)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = param,
                    UseShellExecute = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = false
                }
            };
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode;
        }

        public static async Task<int> RunProcessAsAdmin(string path, string param)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = param,
                    UseShellExecute = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = false,
                    Verb = "runas"
                }
            };
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode;
        }

        public static string md5(this byte[] content, Encoding encoding = null)
        {
            using (var sha = System.Security.Cryptography.MD5.Create())
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
    }
}
