using System;
using System.Collections.Generic;

namespace client
{
    public class StudentCsvRow
    {
        public string Key { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
    }

    public static class StudentCsvProcessor
    {
        /// <summary>
        /// Parses lines of CSV and extracts key-studentName mappings.
        /// </summary>
        public static List<StudentCsvRow> ParseCsv(IEnumerable<string> lines)
        {
            var results = new List<StudentCsvRow>();
            if (lines == null) return results;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (parts.Length < 2) continue;

                string key = parts[0].Trim().Trim('"', '\'');
                string studentName = parts[1].Trim().Trim('"', '\'');

                if (string.IsNullOrEmpty(key)) continue;

                results.Add(new StudentCsvRow
                {
                    Key = key,
                    StudentName = studentName
                });
            }

            return results;
        }

        /// <summary>
        /// Escapes a CSV field to handle commas, quotes, and newlines properly.
        /// </summary>
        public static string EscapeField(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Contains(",") || text.Contains("\"") || text.Contains("\r") || text.Contains("\n"))
            {
                return $"\"{text.Replace("\"", "\"\"")}\"";
            }
            return text;
        }

        /// <summary>
        /// Formats single CSV output row from properties.
        /// </summary>
        public static string BuildCsvRow(string key, string studentName, string ipAddress, string machineName, string group)
        {
            return $"{EscapeField(key)},{EscapeField(studentName)},{EscapeField(ipAddress)},{EscapeField(machineName)},{EscapeField(group)}";
        }
    }
}
