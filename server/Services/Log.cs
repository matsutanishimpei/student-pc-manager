using System;
using System.IO;

namespace Server.Services
{
    public static class Log
    {
        public static void Write(string message)
        {
            try
            {
                string logPath = "C:\\Users\\Public\\sendCMD_server_log.txt";
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch {}
        }
    }
}
