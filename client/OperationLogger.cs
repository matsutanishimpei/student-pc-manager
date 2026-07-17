using System;
using System.IO;
using System.Text;

namespace client
{
    internal sealed class OperationLogger
    {
        private readonly string _logPath;
        private readonly object _writeLock = new();

        public OperationLogger(string localApplicationDataPath)
        {
            string directory = Path.Combine(localApplicationDataPath, "sendCMD");
            _logPath = Path.Combine(directory, "client_operation_log.txt");
        }

        public void Write(string message)
        {
            lock (_writeLock)
            {
                string? directory = Path.GetDirectoryName(_logPath);
                if (directory != null)
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(
                    _logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
    }
}
