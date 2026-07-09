using System;
using System.IO;

namespace Server.Services
{
    public static class HelperLifecycleService
    {
        public static void RegisterAndStartHelper()
        {
            try
            {
                string helperPath = Path.Combine(AppContext.BaseDirectory, "sendCMD_helper.exe");
                if (!File.Exists(helperPath))
                {
                    Log.Write($"[Helper Startup] sendCMD_helper.exe not found at {helperPath}");
                    return;
                }

                // 1. HKLM Run レジストリキーに登録
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true))
                        {
                            if (key != null)
                            {
                                key.SetValue("sendCMD_helper", $"\"{helperPath}\"");
                                Log.Write("[Helper Startup] Registered sendCMD_helper in HKLM Run registry key");
                            }
                            else
                            {
                                Log.Write("[Helper Startup] Failed to open HKLM Run registry key");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write($"[Helper Startup] Registry registration failed: {ex.Message}");
                }

                // 2. 現在ログイン中のユーザーセッションがあれば、CreateProcessAsUser で helper.exe を即起動 (非同期)
                try
                {
                    Log.Write("[Helper Startup] Attempting to start sendCMD_helper in active user session...");
                    var (success, error) = InteractiveProcessHelper.RunInUserSession($"\"{helperPath}\"", 5000, wait: false);
                    if (success)
                    {
                        Log.Write("[Helper Startup] sendCMD_helper launch command sent successfully (wait: false)");
                    }
                    else
                    {
                        Log.Write($"[Helper Startup] sendCMD_helper launch failed or no active session: {error}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Write($"[Helper Startup] Exception during user session launch: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Write($"[Helper Startup] Exception in RegisterAndStartHelper: {ex.Message}");
            }
        }
    }
}
