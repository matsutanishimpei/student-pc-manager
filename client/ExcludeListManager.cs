using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace client
{
    public static class ExcludeListManager
    {
        private static readonly string ExcludeFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "sendCMD",
            "client_exclude_processes.txt"
        );

        private static readonly HashSet<string> _excludedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static ExcludeListManager()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                _excludedProcesses.Clear();
                if (File.Exists(ExcludeFilePath))
                {
                    var lines = File.ReadAllLines(ExcludeFilePath, Encoding.UTF8);
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            _excludedProcesses.Add(trimmed);
                        }
                    }
                }
            }
            catch {}
        }

        public static void Save()
        {
            try
            {
                string? dir = Path.GetDirectoryName(ExcludeFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllLines(ExcludeFilePath, _excludedProcesses, Encoding.UTF8);
            }
            catch {}
        }

        public static bool IsExcluded(string processName)
        {
            return _excludedProcesses.Contains(processName);
        }

        public static void Add(string processName)
        {
            if (_excludedProcesses.Add(processName))
            {
                Save();
            }
        }

        public static void Remove(string processName)
        {
            if (_excludedProcesses.Remove(processName))
            {
                Save();
            }
        }

        public static List<string> GetList()
        {
            return _excludedProcesses.ToList();
        }

        public static string GetFilePath() => ExcludeFilePath;
    }
}
