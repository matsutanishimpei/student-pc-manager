using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Share.Models;
using System.Text.Json;

namespace client
{
    public partial class ProcessViewerWindow : Window
    {
        private readonly string _pcAddress;
        private readonly string _apiKey;
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public ProcessViewerWindow(string pcAddress, string apiKey)
        {
            InitializeComponent();
            _pcAddress = pcAddress;
            _apiKey = apiKey;
            TargetPcTextBlock.Text = _pcAddress;
            
            Loaded += async (s, e) => await LoadProcessesAsync();
        }

        private async Task LoadProcessesAsync()
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = "プロセス情報を更新中...";
                ProcessListView.IsEnabled = false;
            });

            try
            {
                // ウィンドウタイトルを持つプロセスのみを取得するPowerShellコマンド
                string command = "$p = Get-Process | Where-Object { $_.MainWindowTitle } | Select-Object ProcessName, Id, MainWindowTitle; if ($p) { ConvertTo-Json @($p) -Compress } else { \"[]\" }";
                
                var reqObj = new CommandRequest { Command = command };
                var request = new HttpRequestMessage(HttpMethod.Post, $"http://{_pcAddress}/api/exec")
                {
                    Content = JsonContent.Create(reqObj)
                };
                request.Headers.Add("X-API-KEY", _apiKey);

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var cmdResult = await response.Content.ReadFromJsonAsync<CommandResponse>();
                    if (cmdResult != null && cmdResult.ExitCode == 0)
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var processes = JsonSerializer.Deserialize<List<ProcessInfo>>(cmdResult.Stdout, options);
                        
                        Dispatcher.Invoke(() =>
                        {
                            ProcessListView.ItemsSource = processes;
                            StatusTextBlock.Text = $"取得完了: {processes?.Count ?? 0} 個のプロセスが実行中";
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() => StatusTextBlock.Text = $"取得失敗。エラー: {cmdResult?.Stderr}");
                    }
                }
                else
                {
                    string err = await response.Content.ReadAsStringAsync();
                    Dispatcher.Invoke(() => StatusTextBlock.Text = $"通信エラー: HTTP {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => StatusTextBlock.Text = $"接続エラー: {ex.Message}");
            }
            finally
            {
                Dispatcher.Invoke(() => ProcessListView.IsEnabled = true);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadProcessesAsync();
        }

        private async void KillProcessButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int pid)
            {
                if (MessageBox.Show($"PID {pid} のプロセスを強制終了しますか？", "プロセスの終了確認", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                StatusTextBlock.Text = $"PID {pid} を強制終了中...";
                try
                {
                    string command = $"Stop-Process -Id {pid} -Force";
                    var reqObj = new CommandRequest { Command = command };
                    var request = new HttpRequestMessage(HttpMethod.Post, $"http://{_pcAddress}/api/exec")
                    {
                        Content = JsonContent.Create(reqObj)
                    };
                    request.Headers.Add("X-API-KEY", _apiKey);

                    var response = await httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        var cmdResult = await response.Content.ReadFromJsonAsync<CommandResponse>();
                        if (cmdResult != null && cmdResult.ExitCode == 0)
                        {
                            MessageBox.Show("プロセスを終了しました。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                            await LoadProcessesAsync(); // リストを更新
                        }
                        else
                        {
                            MessageBox.Show($"プロセスの終了に失敗しました: {cmdResult?.Stderr}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"サーバー通信エラーが発生しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"エラー: {ex.Message}", "例外発生", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    public class ProcessInfo
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string MainWindowTitle { get; set; } = string.Empty;
    }
}
