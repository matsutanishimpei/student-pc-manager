using System;
using System.IO;
using System.Text;
using Share.Security;

namespace Server.Services
{
    public static class Log
    {
        internal const string LogPath = "C:\\Users\\Public\\sendCMD_server_log.txt";
        internal const long MaxLogBytes = 10 * 1024 * 1024;
        private static readonly object SyncRoot = new();

        public static void Write(string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    RotateIfNeeded();
                    File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n", new UTF8Encoding(false));
                    WindowsFileSecurity.RestrictToAdministratorsAndSystem(LogPath, includeCurrentUser: false);
                }
            }
            catch {}
        }

        private static void RotateIfNeeded()
        {
            if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes)
            {
                return;
            }

            File.Move(LogPath, LogPath + ".1", overwrite: true);
        }
    }
}
