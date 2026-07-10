using System;
using System.Collections.Generic;

namespace client
{
    public static class PcNameGenerator
    {
        /// <summary>
        /// Generates a list of PC host/address strings based on naming rules.
        /// </summary>
        /// <param name="prefix">Host prefix, e.g. "PC-STUDENT-"</param>
        /// <param name="startNum">Starting number</param>
        /// <param name="endNum">Ending number</param>
        /// <param name="digits">Number of digits for zero-padding</param>
        /// <param name="portText">Optional port number, e.g. "5000"</param>
        /// <returns>A list of formatted host/IP strings</returns>
        public static List<string> GenerateNames(string prefix, int startNum, int endNum, int digits, string portText)
        {
            var names = new List<string>();
            if (startNum > endNum || digits <= 0)
            {
                return names;
            }

            for (int i = startNum; i <= endNum; i++)
            {
                string numStr = i.ToString().PadLeft(digits, '0');
                string host = $"{prefix}{numStr}";
                if (!string.IsNullOrEmpty(portText))
                {
                    host += $":{portText}";
                }
                names.Add(host);
            }

            return names;
        }
    }
}
