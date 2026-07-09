using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Share.Models;

namespace Server.Services
{
    public class AdaptiveSessionExecutor : IInteractiveTaskExecutor
    {
        private readonly IConfiguration _configuration;
        private readonly HashSet<string> _excludeProcesses;

        public AdaptiveSessionExecutor(IConfiguration configuration)
        {
            _configuration = configuration;
            var excludeList = _configuration.GetSection("ExcludeProcesses").Get<string[]>() ?? Array.Empty<string>();
            _excludeProcesses = new HashSet<string>(excludeList, StringComparer.OrdinalIgnoreCase);
        }

        private const string ScreenshotPsCommand =
            "try { " +
            "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; " +
            "$s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; " +
            "$b = New-Object System.Drawing.Bitmap $s.Width, $s.Height; " +
            "$g = [System.Drawing.Graphics]::FromImage($b); " +
            "$g.CopyFromScreen($s.X, $s.Y, 0, 0, $b.Size); " +
            "$b.Save('{OUT_FILE}', [System.Drawing.Imaging.ImageFormat]::Jpeg); " +
            "$g.Dispose(); $b.Dispose();" +
            "} catch {" +
            "\"[Screenshot Error] $_\" | Out-File -FilePath 'C:\\Users\\Public\\sendCMD_US_error_log.txt' -Append -Encoding utf8" +
            "}";

        private const string ProcessesPsCommand =
            "$p = Get-Process | Where-Object { $_.MainWindowTitle } | Select-Object ProcessName, Id, MainWindowTitle; if ($p) { ConvertTo-Json @($p) -Compress } else { '[]' }";

        public async Task<byte[]?> GetScreenshotAsync()
        {
            // 1. Try Helper Process via Named Pipe
            byte[]? data = await HelperPipeClient.SendCommandAsync("screenshot", timeoutMs: 500);
            if (data != null && data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                return data;
            }

            if (data != null)
            {
                string errorMsg = Encoding.UTF8.GetString(data);
                Log.Write($"[Helper Screenshot Failed] Helper returned non-JPEG data (length {data.Length}): {errorMsg}");
            }

            // 2. Fallback to PowerShell
            Log.Write("[Fallback] Screenshot requested via PowerShell fallback");
            return await ExecutePowerShellInUserSessionAsync(ScreenshotPsCommand, "jpg");
        }

        public async Task<string> GetActiveAppAsync()
        {
            string processesJson = await GetProcessesJsonAsync();
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var processes = System.Text.Json.JsonSerializer.Deserialize<ProcessItem[]>(processesJson, options);
                if (processes == null) return string.Empty;

                var titles = processes
                    .Select(p => p.MainWindowTitle)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                return string.Join(", ", titles);
            }
            catch (Exception ex)
            {
                Log.Write($"[ActiveApp Filter Error] Failed to generate active app title: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task<string> GetProcessesJsonAsync()
        {
            // 1. Try Helper Process via Named Pipe
            byte[]? data = await HelperPipeClient.SendCommandAsync("processes", timeoutMs: 500);
            string json;
            if (data != null)
            {
                json = Encoding.UTF8.GetString(data).Trim();
            }
            else
            {
                // 2. Fallback to PowerShell
                Log.Write("[Fallback] Processes requested via PowerShell fallback");
                byte[]? psData = await ExecutePowerShellInUserSessionAsync(ProcessesPsCommand, "txt");
                json = psData != null ? Encoding.UTF8.GetString(psData).Trim() : "[]";
            }

            return FilterProcessesJson(json);
        }

        internal string FilterProcessesJson(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson) || rawJson == "[]" || _excludeProcesses.Count == 0)
            {
                return rawJson;
            }

            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var processes = System.Text.Json.JsonSerializer.Deserialize<ProcessItem[]>(rawJson, options);
                if (processes == null) return "[]";

                var filtered = processes
                    .Where(p => !_excludeProcesses.Contains(p.ProcessName))
                    .ToList();

                return System.Text.Json.JsonSerializer.Serialize(filtered);
            }
            catch (Exception ex)
            {
                Log.Write($"[Process Filter Error] Failed to filter processes: {ex.Message}");
                return rawJson;
            }
        }

        internal class ProcessItem
        {
            public string ProcessName { get; set; } = string.Empty;
            public int Id { get; set; }
            public string MainWindowTitle { get; set; } = string.Empty;
        }

        public async Task<CommandResponse> ExecuteCommandAsync(string command, bool runInUserSession)
        {
            Log.Write($"[Command Execution] RunInUserSession: {runInUserSession}, Command: {command}");
            if (runInUserSession)
            {
                byte[]? bytes = await ExecutePowerShellInUserSessionAsync(command, "txt");
                if (bytes == null)
                {
                    return new CommandResponse
                    {
                        ExitCode = -1,
                        Stderr = "Failed to execute PowerShell command in interactive user session. (Check logs at C:\\Users\\Public\\sendCMD_server_log.txt)"
                    };
                }
                string output = Encoding.UTF8.GetString(bytes).Trim();
                return new CommandResponse
                {
                    ExitCode = 0,
                    Stdout = output
                };
            }
            else
            {
                return ExecutePowerShell(command);
            }
        }

        // --- PowerShell Execution Helpers ---

        private static CommandResponse ExecutePowerShell(string command)
        {
            var result = new CommandResponse();
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -";
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.Start();

                using (var sw = process.StandardInput)
                {
                    if (sw.BaseStream.CanWrite)
                    {
                        sw.WriteLine(command);
                    }
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();

                process.WaitForExit();

                result.ExitCode = process.ExitCode;
                result.Stdout = stdout;
                result.Stderr = stderr;
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Stderr = ex.Message;
            }
            return result;
        }

        private static async Task<byte[]?> ExecutePowerShellInUserSessionAsync(string psCommand, string fileExtension)
        {
            int sessionId = 0;
            try
            {
                sessionId = Process.GetCurrentProcess().SessionId;
            }
            catch {}

            if (sessionId != 0)
            {
                try
                {
                    string tempOutFile = Path.Combine("C:\\Users\\Public", "sendCMD_direct_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "." + fileExtension);
                    string directScript;
                    if (fileExtension == "jpg" || fileExtension == "png")
                    {
                        directScript = psCommand.Replace("{OUT_FILE}", tempOutFile.Replace("\\", "\\\\"));
                    }
                    else
                    {
                        directScript = $"$r = $({psCommand}); Out-File -FilePath '{tempOutFile}' -InputObject $r -Encoding utf8";
                    }

                    var resp = ExecutePowerShell(directScript);
                    if (resp.ExitCode == 0 && File.Exists(tempOutFile))
                    {
                        byte[] bytes = await File.ReadAllBytesAsync(tempOutFile);
                        try { File.Delete(tempOutFile); } catch {}
                        return bytes;
                    }
                    else
                    {
                        Log.Write($"[Direct Execution Error] ExitCode: {resp.ExitCode}, Stderr: {resp.Stderr}");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write($"[Direct Execution Exception] {ex.Message}");
                    return null;
                }
            }

            string taskId = "sendCMD_US_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string scriptFile = Path.Combine("C:\\Users\\Public", taskId + ".ps1");
            string outputFile = Path.Combine("C:\\Users\\Public", taskId + "." + fileExtension);

            string fullScriptContent;
            if (fileExtension == "jpg" || fileExtension == "png")
            {
                fullScriptContent = psCommand.Replace("{OUT_FILE}", outputFile);
            }
            else
            {
                fullScriptContent = $"$ErrorActionPreference = 'Continue'; $r = & {{ {psCommand} }} 2>&1 | Out-String; Out-File -FilePath '{outputFile}' -InputObject $r -Encoding utf8";
            }

            try
            {
                await File.WriteAllTextAsync(scriptFile, fullScriptContent, Encoding.UTF8);

                string cmdLine = $"powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptFile}\"";
                var (success, error) = InteractiveProcessHelper.RunInUserSession(cmdLine, 15000);

                try { File.Delete(scriptFile); } catch {}

                if (!success)
                {
                    Log.Write($"[CreateProcessAsUser FAILED] {error}");
                    return null;
                }

                if (!File.Exists(outputFile) || new FileInfo(outputFile).Length == 0)
                {
                    Log.Write($"[Output File Missing] {outputFile} was not created or is empty after CreateProcessAsUser");
                    return null;
                }

                byte[] resultBytes = await File.ReadAllBytesAsync(outputFile);
                try { File.Delete(outputFile); } catch {}
                return resultBytes;
            }
            catch (Exception ex)
            {
                Log.Write($"[Session 0 Execution Exception] {ex.Message}");
                try { File.Delete(scriptFile); } catch {}
                return null;
            }
        }
    }
}
