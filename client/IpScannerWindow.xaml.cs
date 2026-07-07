using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Net.NetworkInformation;
using Share.Models;

namespace client
{
    public partial class IpScannerWindow : Window
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _cts;
        private bool _isScanning = false;
        private List<string> _localIps = new List<string>();

        public ObservableCollection<ScanResultItem> ScanResults { get; set; } = new ObservableCollection<ScanResultItem>();
        public List<string> SelectedHosts { get; private set; } = new List<string>();
        public List<ScanResultItem> SelectedItems { get; private set; } = new List<ScanResultItem>();

        public IpScannerWindow(string apiKey, HttpClient httpClient)
        {
            InitializeComponent();
            _apiKey = apiKey;
            _httpClient = httpClient;
            ResultsListView.ItemsSource = ScanResults;
        }

        private async void StartScanButton_Click(object sender, RoutedEventArgs e)
        {
            string startIpStr = StartIpTextBox.Text.Trim();
            string endIpStr = EndIpTextBox.Text.Trim();
            string portStr = PortTextBox.Text.Trim();

            if (!IPAddress.TryParse(startIpStr, out var startIp) || !IPAddress.TryParse(endIpStr, out var endIp))
            {
                MessageBox.Show("IPアドレスのフォーマットが正しくありません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("ポート番号は1から65535の間で指定してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var ips = GenerateIpRange(startIp, endIp);
            if (ips.Count == 0)
            {
                MessageBox.Show("開始IPは終了IP以下である必要があります。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 自PCのローカルIPアドレスを取得しておく
            try
            {
                _localIps = Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToList();
            }
            catch
            {
                _localIps.Clear();
            }

            // UI状態の更新
            SetUiScanningState(true);
            ScanResults.Clear();
            ScanProgressBar.Maximum = ips.Count;
            ScanProgressBar.Value = 0;
            ProgressTextBlock.Text = $"スキャンを開始します... (全 {ips.Count} 台)";

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            int scannedCount = 0;
            int foundCount = 0;

            try
            {
                // 最大30並列でスキャン
                using (var semaphore = new SemaphoreSlim(30))
                {
                    var tasks = ips.Select(async ip =>
                    {
                        try
                        {
                            await semaphore.WaitAsync(token);
                            if (token.IsCancellationRequested) return;

                            var result = await CheckHostAsync(ip, port, token);
                            
                            // UI更新
                            Dispatcher.Invoke(() =>
                            {
                                if (result != null)
                                {
                                    ScanResults.Add(result);
                                    foundCount++;
                                }
                                scannedCount++;
                                ScanProgressBar.Value = scannedCount;
                                ProgressTextBlock.Text = $"スキャン中: {scannedCount} / {ips.Count} (検出: {foundCount} 台)";
                            });
                        }
                        catch (OperationCanceledException)
                        {
                            // キャンセル時は無視
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(tasks);
                }

                ProgressTextBlock.Text = $"スキャン完了。 {foundCount} 台のPCを検出しました。";
            }
            catch (Exception ex)
            {
                ProgressTextBlock.Text = $"スキャンエラー: {ex.Message}";
            }
            finally
            {
                SetUiScanningState(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void StopScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning)
            {
                _cts?.Cancel();
                ProgressTextBlock.Text = "スキャンを停止しています...";
            }
        }

        private async Task<ScanResultItem?> CheckHostAsync(string ip, int port, CancellationToken token)
        {
            try
            {
                // 0. 自PCのIPアドレス判定
                if (_localIps.Contains(ip))
                {
                    return new ScanResultItem
                    {
                        IsSelected = true,
                        IpAddress = $"{ip}:{port}",
                        MachineName = Environment.MachineName,
                        Status = "オンライン (自PC)"
                    };
                }

                // 1. ローカル名前解決を試みる (DNS / LLMNR / mDNS / NetBIOS)
                // 1.0秒のタイムアウト付き
                var hostEntry = await GetHostEntryWithTimeoutAsync(ip, TimeSpan.FromMilliseconds(1000), token);
                if (hostEntry != null && !string.IsNullOrEmpty(hostEntry.HostName))
                {
                    string hostName = hostEntry.HostName;
                    // ドメイン名部分 (.local や .corp など) が含まれる場合は最初のピリオドまでを取得
                    int dotIndex = hostName.IndexOf('.');
                    if (dotIndex > 0)
                    {
                        hostName = hostName.Substring(0, dotIndex);
                    }

                    // Windowsの名前解決で、名前解決できなかった場合にIPアドレス自体がHostNameに入ることがある
                    if (hostName != ip)
                    {
                        return new ScanResultItem
                        {
                            IsSelected = true,
                            IpAddress = $"{ip}:{port}",
                            MachineName = hostName,
                            Status = "オンライン"
                        };
                    }
                }

                // 2. 名前解決ができなかった場合、TCPポート接続試行で生存確認 (TCP 135 / 445 / 5000)
                if (token.IsCancellationRequested) return null;

                bool isAlive = await IsPortOpenAsync(ip, 135, 300, token) || 
                               await IsPortOpenAsync(ip, 445, 300, token) ||
                               await IsPortOpenAsync(ip, port, 300, token);

                // 3. TCPポートが応答しない場合、最後の手段として Ping (ICMP) で生存確認
                if (!isAlive)
                {
                    if (token.IsCancellationRequested) return null;
                    isAlive = await PingHostAsync(ip, 300, token);
                }

                if (isAlive)
                {
                    return new ScanResultItem
                    {
                        IsSelected = false, // PC名が取れなかったのでデフォルトでは未選択にする
                        IpAddress = $"{ip}:{port}",
                        MachineName = "(名前解決不可)",
                        Status = "応答あり (IPのみ)"
                    };
                }
            }
            catch
            {
                // 接続失敗やタイムアウト
            }
            return null;
        }

        private async Task<bool> IsPortOpenAsync(string ip, int port, int timeoutMs, CancellationToken token)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    if (IPAddress.TryParse(ip, out var ipAddr))
                    {
                        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            cts.CancelAfter(timeoutMs);
                            await client.ConnectAsync(ipAddr, port, cts.Token);
                            return true; // 接続成功
                        }
                    }
                }
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                // 接続拒否 (RST) の場合、ホストは生存している
                if (ex.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused)
                {
                    return true;
                }
            }
            catch
            {
                // タイムアウトなど
            }
            return false;
        }

        private async Task<IPHostEntry?> GetHostEntryWithTimeoutAsync(string ip, TimeSpan timeout, CancellationToken token)
        {
            try
            {
                var hostEntryTask = Dns.GetHostEntryAsync(ip);
                var delayTask = Task.Delay(timeout, token);
                var completedTask = await Task.WhenAny(hostEntryTask, delayTask);
                if (completedTask == hostEntryTask)
                {
                    return await hostEntryTask;
                }
            }
            catch
            {
                // 名前解決失敗
            }
            return null;
        }

        private async Task<bool> PingHostAsync(string ip, int timeoutMs, CancellationToken token)
        {
            try
            {
                using (var ping = new Ping())
                {
                    if (IPAddress.TryParse(ip, out var ipAddr))
                    {
                        var reply = await ping.SendPingAsync(ipAddr, TimeSpan.FromMilliseconds(timeoutMs), Array.Empty<byte>(), (PingOptions?)null, token);
                        return reply.Status == IPStatus.Success;
                    }
                }
            }
            catch
            {
                // Ping失敗
            }
            return false;
        }

        private List<string> GenerateIpRange(IPAddress start, IPAddress end)
        {
            var list = new List<string>();
            byte[] startBytes = start.GetAddressBytes();
            byte[] endBytes = end.GetAddressBytes();

            if (startBytes.Length != 4 || endBytes.Length != 4) return list;

            uint startVal = (uint)(startBytes[0] << 24 | startBytes[1] << 16 | startBytes[2] << 8 | startBytes[3]);
            uint endVal = (uint)(endBytes[0] << 24 | endBytes[1] << 16 | endBytes[2] << 8 | endBytes[3]);

            if (startVal > endVal) return list;

            for (uint i = startVal; i <= endVal; i++)
            {
                byte[] bytes = new byte[] {
                    (byte)((i >> 24) & 0xFF),
                    (byte)((i >> 16) & 0xFF),
                    (byte)((i >> 8) & 0xFF),
                    (byte)(i & 0xFF)
                };
                list.Add(new IPAddress(bytes).ToString());
            }

            return list;
        }

        private void SetUiScanningState(bool scanning)
        {
            _isScanning = scanning;
            StartScanButton.IsEnabled = !scanning;
            StopScanButton.IsEnabled = scanning;
            StartIpTextBox.IsEnabled = !scanning;
            EndIpTextBox.IsEnabled = !scanning;
            PortTextBox.IsEnabled = !scanning;
            RegByNameRadio.IsEnabled = !scanning;
            RegByIpRadio.IsEnabled = !scanning;
            RegisterButton.IsEnabled = !scanning;
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in ScanResults)
            {
                item.IsSelected = true;
            }
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in ScanResults)
            {
                item.IsSelected = false;
            }
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ScanResults.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("登録するPCが選択されていません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool regByName = RegByNameRadio.IsChecked == true;
            string portText = PortTextBox.Text.Trim();

            foreach (var item in selected)
            {
                if (regByName && !string.IsNullOrEmpty(item.MachineName) && item.MachineName != "(認証エラー)")
                {
                    SelectedHosts.Add($"{item.MachineName}:{portText}");
                }
                else
                {
                    SelectedHosts.Add(item.IpAddress);
                }
            }
            SelectedItems = selected;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            DialogResult = false;
            Close();
        }
    }

    public class ScanResultItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _ipAddress = string.Empty;
        private string _machineName = string.Empty;
        private string _status = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string IpAddress
        {
            get => _ipAddress;
            set { _ipAddress = value; OnPropertyChanged(); }
        }

        public string MachineName
        {
            get => _machineName;
            set { _machineName = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
