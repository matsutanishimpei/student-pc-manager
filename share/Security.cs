using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Share.Security
{
    public static class ApiSignature
    {
        public const string EmptyContentHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        public static string Generate(string apiKey, string timestamp, string nonce, string method, string path, string contentHash)
            => Generate(apiKey, timestamp, nonce, method, path, contentHash, EmptyContentHash);

        public static string Generate(string apiKey, string timestamp, string nonce, string method, string path, string contentHash, string fileNameHash)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                return string.Empty;
            }

            // メソッドとパスを標準化
            string rawData = $"{timestamp}:{nonce}:{method.ToUpperInvariant()}:{path}:{contentHash.ToLowerInvariant()}:{fileNameHash.ToLowerInvariant()}";
            byte[] keyBytes = Encoding.UTF8.GetBytes(apiKey);
            byte[] dataBytes = Encoding.UTF8.GetBytes(rawData);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                byte[] hashBytes = hmac.ComputeHash(dataBytes);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
        }

        public static bool Verify(string apiKey, string timestamp, string nonce, string method, string path, string contentHash, string signature, long maxTimeDriftSeconds = 300)
            => Verify(apiKey, timestamp, nonce, method, path, contentHash, EmptyContentHash, signature, maxTimeDriftSeconds);

        public static bool Verify(string apiKey, string timestamp, string nonce, string method, string path, string contentHash, string fileNameHash, string signature, long maxTimeDriftSeconds = 300)
        {
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce) ||
                string.IsNullOrEmpty(contentHash) || string.IsNullOrEmpty(fileNameHash) || string.IsNullOrEmpty(signature))
            {
                return false;
            }

            // タイムスタンプの検証 (タイムウィンドウ制限によるリプレイ攻撃防止)
            if (!long.TryParse(timestamp, out long requestTime))
            {
                return false;
            }

            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (maxTimeDriftSeconds < 0 ||
                requestTime < currentTime - maxTimeDriftSeconds ||
                requestTime > currentTime + maxTimeDriftSeconds)
            {
                return false;
            }

            // 期待される署名の計算
            string expectedSignature = Generate(apiKey, timestamp, nonce, method, path, contentHash, fileNameHash);

            // 定数時間比較によるタイミング攻撃防止
            byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
            byte[] signatureBytes = Encoding.UTF8.GetBytes(signature);

            if (expectedBytes.Length != signatureBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(expectedBytes, signatureBytes);
        }

        public static async Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
