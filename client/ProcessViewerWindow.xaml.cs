using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Share.Models;
using System.Text.Json;
using Microsoft.Win32;

namespace client
{
    public partial class ProcessViewerWindow : Window
    {
        private readonly string _pcAddress;
        private readonly string _apiKey;
        private byte[]? _currentImageBytes;
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public ProcessViewerWindow(string pcAddress, string apiKey)
        {
            InitializeComponent();
            _pcAddress = pcAddress;
            _apiKey = apiKey;
            TargetPcTextBlock.Text = _pcAddress;
            
            Loaded += async (s, e) => {
                await LoadProcessesAsync();
                // 起動時に自動で1回キャプチャも取得する
                await CaptureScreenAsync();
            };
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
                var request = new HttpRequestMessage(HttpMethod.Get, $"http://{_pcAddress}/api/processes");
                request.Headers.Add("X-API-KEY", _apiKey);

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var processes = await response.Content.ReadFromJsonAsync<List<ProcessInfo>>(options);
                    
                    Dispatcher.Invoke(() =>
                    {
                        ProcessListView.ItemsSource = processes;
                        StatusTextBlock.Text = $"取得完了: {processes?.Count ?? 0} 個のプロセスが実行中";
                    });
                }
                else
                {
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

        private async Task CaptureScreenAsync()
        {
            Dispatcher.Invoke(() =>
            {
                ImageStatusTextBlock.Text = "キャプチャ取得中...";
                ImageStatusTextBlock.Visibility = Visibility.Visible;
                ScreenshotImage.Source = null;
                SaveImageButton.IsEnabled = false;
            });

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"http://{_pcAddress}/api/screenshot");
                request.Headers.Add("X-API-KEY", _apiKey);

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
                    _currentImageBytes = imageBytes;

                    var bitmap = new BitmapImage();
                    using (var ms = new MemoryStream(imageBytes))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                    }
                    bitmap.Freeze();

                    Dispatcher.Invoke(() =>
                    {
                        ScreenshotImage.Source = bitmap;
                        ImageStatusTextBlock.Visibility = Visibility.Collapsed;
                        SaveImageButton.IsEnabled = true;
                    });
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
                {
                    Dispatcher.Invoke(() =>
                    {
                        ImageStatusTextBlock.Text = "失敗 (ログインユーザー不在 / ロック画面)";
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        ImageStatusTextBlock.Text = $"取得失敗 (HTTP {response.StatusCode})";
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ImageStatusTextBlock.Text = $"エラー: {ex.Message}";
                });
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadProcessesAsync();
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            await CaptureScreenAsync();
        }

        private void SaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImageBytes == null) return;

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "JPEG画像 (*.jpg;*.jpeg)|*.jpg;*.jpeg|すべてのファイル (*.*)|*.*",
                FileName = $"{_pcAddress.Replace(":", "_")}_screenshot.jpg"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllBytes(saveFileDialog.FileName, _currentImageBytes);
                    MessageBox.Show("画像を保存しました。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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
                            await LoadProcessesAsync();
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
