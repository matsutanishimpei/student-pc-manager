using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Share.Security;
using Server.Services;

namespace Server.Middlewares
{
    public sealed class ApiNonceStore
    {
        private readonly ConcurrentDictionary<string, long> _nonces = new();
        private long _nextCleanupUnixSeconds;
        private readonly string? _persistencePath;
        private readonly bool _restrictFileAccess;
        private readonly object _syncRoot = new();

        public ApiNonceStore() { }

        public ApiNonceStore(string? persistencePath, bool restrictFileAccess = true)
        {
            _persistencePath = persistencePath;
            _restrictFileAccess = restrictFileAccess;
            LoadPersistedNonces();
        }

        public bool TryUse(string nonce, long expiresAtUnixSeconds)
        {
            lock (_syncRoot)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long nextCleanup = Volatile.Read(ref _nextCleanupUnixSeconds);
                if (now >= nextCleanup && Interlocked.CompareExchange(ref _nextCleanupUnixSeconds, now + 60, nextCleanup) == nextCleanup)
                {
                    foreach (var entry in _nonces)
                    {
                        if (entry.Value < now) _nonces.TryRemove(entry.Key, out _);
                    }
                }

                if (!_nonces.TryAdd(nonce, expiresAtUnixSeconds)) return false;
                if (!Persist())
                {
                    _nonces.TryRemove(nonce, out _);
                    return false;
                }
                return true;
            }
        }

        private void LoadPersistedNonces()
        {
            if (_persistencePath == null || !File.Exists(_persistencePath)) return;
            try
            {
                var persisted = JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(_persistencePath, Encoding.UTF8));
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (persisted != null)
                {
                    foreach (var entry in persisted)
                    {
                        if (entry.Value >= now) _nonces.TryAdd(entry.Key, entry.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write($"[Nonce Cache Load Error] {ex.Message}");
            }
        }

        private bool Persist()
        {
            if (_persistencePath == null) return true;
            try
            {
                string? directory = Path.GetDirectoryName(_persistencePath);
                if (directory != null) Directory.CreateDirectory(directory);
                string temporaryPath = _persistencePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_nonces), new UTF8Encoding(false));
                File.Move(temporaryPath, _persistencePath, overwrite: true);
                if (_restrictFileAccess)
                {
                    WindowsFileSecurity.RestrictToAdministratorsAndSystem(_persistencePath, includeCurrentUser: false);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"[Nonce Cache Save Error] {ex.Message}");
                return false;
            }
        }
    }
}
