using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace client
{
    public class StudentCsvRow
    {
        public string Key { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
    }

    public static class StudentCsvProcessor
    {
        public static string[] ReadAllLines(string path, out string encodingName)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string text = DecodeText(bytes, out encodingName);

            var lines = new List<string>();
            using var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line);
            }

            return lines.ToArray();
        }

        internal static string DecodeText(byte[] bytes, out string encodingName)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encodingName = "UTF-8 (BOM付き)";
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encodingName = "UTF-16 LE";
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encodingName = "UTF-16 BE";
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            try
            {
                var strictUtf8 = new UTF8Encoding(false, true);
                string text = strictUtf8.GetString(bytes);
                encodingName = "UTF-8";
                return text;
            }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                encodingName = "Shift-JIS (CP932)";
                return Encoding.GetEncoding(932).GetString(bytes);
            }
        }

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

                var parts = ParseCsvFields(line);
                if (parts.Length < 2) continue;

                string key = parts[0].Trim();
                string studentName = parts[1].Trim();

                if (string.IsNullOrEmpty(key)) continue;

                results.Add(new StudentCsvRow
                {
                    Key = key,
                    StudentName = studentName
                });
            }

            return results;
        }

        private static string[] ParseCsvFields(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());
            return fields.ToArray();
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
