using System;
using System.Security.Cryptography;
using System.Text;

namespace Share.Security
{
    public static class ApiSignature
    {
        public static string Generate(string apiKey, string timestamp, string method, string path)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                return string.Empty;
            }

            // メソッドとパスを標準化
            string rawData = $"{timestamp}:{method.ToUpperInvariant()}:{path}";
            byte[] keyBytes = Encoding.UTF8.GetBytes(apiKey);
            byte[] dataBytes = Encoding.UTF8.GetBytes(rawData);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                byte[] hashBytes = hmac.ComputeHash(dataBytes);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
        }

        public static bool Verify(string apiKey, string timestamp, string method, string path, string signature, long maxTimeDriftSeconds = 300)
        {
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
            {
                return false;
            }

            // タイムスタンプの検証 (タイムウィンドウ制限によるリプレイ攻撃防止)
            if (!long.TryParse(timestamp, out long requestTime))
            {
                return false;
            }

            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(currentTime - requestTime) > maxTimeDriftSeconds)
            {
                return false;
            }

            // 期待される署名の計算
            string expectedSignature = Generate(apiKey, timestamp, method, path);

            // 定数時間比較によるタイミング攻撃防止
            byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
            byte[] signatureBytes = Encoding.UTF8.GetBytes(signature);

            if (expectedBytes.Length != signatureBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(expectedBytes, signatureBytes);
        }
    }
}
