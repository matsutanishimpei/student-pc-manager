using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Share.Models;
using System.Runtime.CompilerServices;

namespace client
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<PcItem> PcList { get; set; }
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        private static readonly string PcsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pcs.json");

        public ObservableCollection<MonitorItem> MonitorList { get; set; } = new ObservableCollection<MonitorItem>();
        private System.Windows.Threading.DispatcherTimer? _monitorTimer;

        public MainWindow()
        {
            InitializeComponent();
            PcList = LoadPcList();
            PcListBox.ItemsSource = PcList;
            MonitorListBox.ItemsSource = MonitorList;
            InitializeMonitorTimer();
            this.Closed += Window_Closed;
            Log("sendCMD コンソールが初期化されました。");
        }

        private void InitializeMonitorTimer()
        {
            _monitorTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _monitorTimer.Tick += MonitorTimer_Tick;
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            if (_monitorTimer != null)
            {
                _monitorTimer.Stop();
                _monitorTimer = null;
            }
        }

        private void Log(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
            }));
        }

        private ObservableCollection<PcItem> LoadPcList()
        {
            try
            {
                if (File.Exists(PcsFilePath))
                {
                    string json = File.ReadAllText(PcsFilePath);
                    var list = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<PcItem>>(json);
                    if (list != null)
                    {
                        return new ObservableCollection<PcItem>(list);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"IPリストの読み込みに失敗しました: {ex.Message}");
            }

            // デフォルト値のフォールバック
            return new ObservableCollection<PcItem>
            {
                new PcItem { IpAddress = "127.0.0.1:5000", IsSelected = true }
            };
        }

        private void SavePcList()
        {
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string json = System.Text.Json.JsonSerializer.Serialize(PcList, options);
                File.WriteAllText(PcsFilePath, json);
            }
            catch (Exception ex)
            {
                Log($"IPリストの保存に失敗しました: {ex.Message}");
            }
        }

        private void AddPcButton_Click(object sender, RoutedEventArgs e)
        {
            string newPc = NewPcTextBox.Text.Trim();
            if (string.IsNullOrEmpty(newPc)) return;

            // 重複チェック
            if (!PcList.Any(p => p.IpAddress.Equals(newPc, StringComparison.OrdinalIgnoreCase)))
            {
                PcList.Add(new PcItem { IpAddress = newPc, IsSelected = true });
                Log($"ターゲットPCを追加しました: {newPc}");
                SavePcList();
            }
            NewPcTextBox.Text = "127.0.0.1:5000";
        }

        private void BulkGenerateButton_Click(object sender, RoutedEventArgs e)
        {
            string prefix = PrefixTextBox.Text.Trim();
            string portText = PortTextBox.Text.Trim();
            
            if (!int.TryParse(StartNumTextBox.Text, out int startNum) ||
                !int.TryParse(EndNumTextBox.Text, out int endNum) ||
                !int.TryParse(DigitsTextBox.Text, out int digits))
            {
                Log("警告: 開始番号、終了番号、または桁数の入力値が不正です。");
                return;
            }

            if (startNum > endNum)
            {
                Log("警告: 開始番号は終了番号以下である必要があります。");
                return;
            }

            int count = 0;
            for (int i = startNum; i <= endNum; i++)
            {
                string numStr = i.ToString().PadLeft(digits, '0');
                string host = $"{prefix}{numStr}";
                if (!string.IsNullOrEmpty(portText))
                {
                    host += $":{portText}";
                }

                if (!PcList.Any(p => p.IpAddress.Equals(host, StringComparison.OrdinalIgnoreCase)))
                {
                    PcList.Add(new PcItem { IpAddress = host, IsSelected = true });
                    count++;
                }
            }

            if (count > 0)
            {
                Log($"一括生成完了: {count} 台のPCをリストに追加しました。");
                SavePcList();
            }
            else
            {
                Log("一括生成完了: 新しく追加されたPCはありません (すべて登録済み)。");
            }
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var pc in PcList)
            {
                pc.IsSelected = true;
            }
            Log("すべてのPCを選択しました。");
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var pc in PcList)
            {
                pc.IsSelected = false;
            }
            Log("すべてのPCの選択を解除しました。");
        }

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = PcList.Where(p => p.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                Log("警告: 削除対象（チェックが入っているPC）がありません。");
                return;
            }

            if (MessageBox.Show($"選択された {selectedItems.Count} 台のPCをリストから削除しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var item in selectedItems)
                {
                    PcList.Remove(item);
                }
                Log($"{selectedItems.Count} 台のPCをリストから削除しました。");
                SavePcList();
            }
        }

        private void BrowseFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "すべてのファイル (*.*)|*.*|MSI インストーラー (*.msi)|*.msi|実行ファイル (*.exe)|*.exe"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                FilePathTextBox.Text = openFileDialog.FileName;
                Log($"ファイルを選択しました: {openFileDialog.FileName}");
            }
        }

        private void ExecuteCommandButton_Click(object sender, RoutedEventArgs e)
        {
            string command = CommandTextBox.Text.Trim();
            if (string.IsNullOrEmpty(command))
            {
                Log("警告: コマンドが空です。");
                return;
            }

            var targets = PcList.Where(p => p.IsSelected).ToList();
            if (!targets.Any())
            {
                Log("警告: 対象PCが選択されていません。");
                return;
            }

            string apiKey = ApiKeyTextBox.Text;

            Log($"{targets.Count} 台のPCでコマンド実行を開始します...");

            foreach (var target in targets)
            {
                // UIスレッドをブロックしないよう非同期タスクとして実行
                _ = Task.Run(async () =>
                {
                    Log($"[{target.IpAddress}] コマンド実行中...");
                    try
                    {
                        var reqObj = new CommandRequest { Command = command };
                        var request = new HttpRequestMessage(HttpMethod.Post, $"http://{target.IpAddress}/api/exec")
                        {
                            Content = JsonContent.Create(reqObj)
                        };
                        request.Headers.Add("X-API-KEY", apiKey);

                        var response = await httpClient.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            var result = await response.Content.ReadFromJsonAsync<CommandResponse>();
                            if (result != null)
                            {
                                Log($"[{target.IpAddress}] 実行完了。ExitCode: {result.ExitCode}");
                                if (!string.IsNullOrEmpty(result.Stdout))
                                {
                                    Log($"[{target.IpAddress}] 出力 (STDOUT):{Environment.NewLine}{result.Stdout.Trim()}");
                                }
                                if (!string.IsNullOrEmpty(result.Stderr))
                                {
                                    Log($"[{target.IpAddress}] エラー出力 (STDERR):{Environment.NewLine}{result.Stderr.Trim()}");
                                }
                            }
                        }
                        else
                        {
                            string errContent = await response.Content.ReadAsStringAsync();
                            Log($"[{target.IpAddress}] 失敗。HTTPステータス: {response.StatusCode}。詳細: {errContent}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[{target.IpAddress}] エラー: {ex.Message}");
                    }
                });
            }
        }

        private void DistributeFileButton_Click(object sender, RoutedEventArgs e)
        {
            string localPath = FilePathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
            {
                Log("警告: 配布するファイルが正しく指定されていません。");
                return;
            }

            var targets = PcList.Where(p => p.IsSelected).ToList();
            if (!targets.Any())
            {
                Log("警告: 対象PCが選択されていません。");
                return;
            }

            string apiKey = ApiKeyTextBox.Text;
            bool executeAfterUpload = ExecuteAfterUploadCheckBox.IsChecked == true;
            string runArgs = ExecutionArgsTextBox.Text.Trim();
            string fileName = Path.GetFileName(localPath);

            Log($"ファイルの配布を開始します: '{fileName}' -> {targets.Count} 台のPC");

            foreach (var target in targets)
            {
                _ = Task.Run(async () =>
                {
                    Log($"[{target.IpAddress}] ファイルアップロード中...");
                    try
                    {
                        string remotePath = "";
                        
                        // 1. ファイルをアップロード
                        using (var content = new MultipartFormDataContent())
                        using (var fileStream = File.OpenRead(localPath))
                        using (var streamContent = new StreamContent(fileStream))
                        {
                            content.Add(streamContent, "file", fileName);
                            var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"http://{target.IpAddress}/api/upload")
                            {
                                Content = content
                            };
                            uploadRequest.Headers.Add("X-API-KEY", apiKey);

                            var uploadResponse = await httpClient.SendAsync(uploadRequest);
                            if (!uploadResponse.IsSuccessStatusCode)
                            {
                                string errContent = await uploadResponse.Content.ReadAsStringAsync();
                                Log($"[{target.IpAddress}] アップロード失敗。HTTPステータス: {uploadResponse.StatusCode}。詳細: {errContent}");
                                return;
                            }

                            var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
                            remotePath = uploadResult?.FilePath ?? "";
                            Log($"[{target.IpAddress}] アップロード完了。保存先: {remotePath}");
                        }

                        // 2. アップロード後の実行処理
                        if (executeAfterUpload && !string.IsNullOrEmpty(remotePath))
                        {
                            Log($"[{target.IpAddress}] リモートPC上でインストーラーを実行中...");
                            
                            // 拡張子に応じた実行コマンドの構成
                            string installCmd;
                            string ext = Path.GetExtension(remotePath).ToLower();
                            if (ext == ".msi")
                            {
                                // msi の場合は msiexec を使用してサイレントインストール
                                installCmd = $"Start-Process msiexec.exe -ArgumentList \"/i \\\"{remotePath}\\\" {runArgs} /norestart\" -Wait -PassThru";
                            }
                            else
                            {
                                // exe などの場合は直接実行
                                installCmd = $"Start-Process \"{remotePath}\" -ArgumentList \"{runArgs}\" -Wait -PassThru";
                            }

                            var execReqObj = new CommandRequest { Command = installCmd };
                            var execRequest = new HttpRequestMessage(HttpMethod.Post, $"http://{target.IpAddress}/api/exec")
                            {
                                Content = JsonContent.Create(execReqObj)
                            };
                            execRequest.Headers.Add("X-API-KEY", apiKey);

                            var execResponse = await httpClient.SendAsync(execRequest);
                            if (execResponse.IsSuccessStatusCode)
                            {
                                var execResult = await execResponse.Content.ReadFromJsonAsync<CommandResponse>();
                                if (execResult != null)
                                {
                                    Log($"[{target.IpAddress}] リモート実行完了。ExitCode: {execResult.ExitCode}");
                                    if (!string.IsNullOrEmpty(execResult.Stdout))
                                    {
                                        Log($"[{target.IpAddress}] 出力 (STDOUT):{Environment.NewLine}{execResult.Stdout.Trim()}");
                                    }
                                    if (!string.IsNullOrEmpty(execResult.Stderr))
                                    {
                                        Log($"[{target.IpAddress}] エラー出力 (STDERR):{Environment.NewLine}{execResult.Stderr.Trim()}");
                                    }
                                }
                            }
                            else
                            {
                                string errContent = await execResponse.Content.ReadAsStringAsync();
                                Log($"[{target.IpAddress}] リモート実行要求に失敗しました。HTTP: {execResponse.StatusCode}。詳細: {errContent}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[{target.IpAddress}] 処理中にエラーが発生しました: {ex.Message}");
                    }
                });
            }
        }

        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            RefreshActiveApps(isAuto: true);
        }

        private void GetActiveAppsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshActiveApps(isAuto: false);
        }

        private void RefreshActiveApps(bool isAuto)
        {
            var targets = PcList.Where(p => p.IsSelected).ToList();
            if (!targets.Any())
            {
                if (!isAuto)
                {
                    Log("警告: 稼働監視対象のPCが選択されていません。");
                }
                return;
            }

            string apiKey = ApiKeyTextBox.Text;

            // 1. 選択解除されたPCの監視項目を削除
            var targetIps = targets.Select(t => t.IpAddress).ToHashSet();
            for (int i = MonitorList.Count - 1; i >= 0; i--)
            {
                if (!targetIps.Contains(MonitorList[i].PcAddress))
                {
                    MonitorList.RemoveAt(i);
                }
            }

            if (!isAuto)
            {
                Log($"{targets.Count} 台のPCでアクティブアプリ取得を開始します...");
            }

            // 2. 差分更新および新規追加
            foreach (var target in targets)
            {
                var item = MonitorList.FirstOrDefault(m => m.PcAddress == target.IpAddress);
                if (item == null)
                {
                    item = new MonitorItem
                    {
                        PcAddress = target.IpAddress,
                        Status = "取得中...",
                        StatusColor = System.Windows.Media.Brushes.Yellow,
                        ActiveApp = ""
                    };
                    MonitorList.Add(item);
                }
                else if (!isAuto)
                {
                    item.Status = "取得中...";
                    item.StatusColor = System.Windows.Media.Brushes.Yellow;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, $"http://{target.IpAddress}/api/activeapp");
                        request.Headers.Add("X-API-KEY", apiKey);

                        var response = await httpClient.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            var result = await response.Content.ReadFromJsonAsync<ActiveAppResponse>();
                            string activeApp = result?.ActiveApp ?? "";
                            if (string.IsNullOrEmpty(activeApp))
                            {
                                activeApp = "(アクティブウィンドウなし / アイドル)";
                            }

                            Dispatcher.Invoke(() =>
                            {
                                item.Status = "オンライン";
                                item.StatusColor = System.Windows.Media.Brushes.Green;
                                item.ActiveApp = activeApp;
                            });
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                item.Status = "接続失敗";
                                item.StatusColor = System.Windows.Media.Brushes.Red;
                                item.ActiveApp = $"HTTP {response.StatusCode}";
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            item.Status = "オフライン";
                            item.StatusColor = System.Windows.Media.Brushes.Gray;
                            item.ActiveApp = ex.Message;
                        });
                    }
                });
            }
        }

        private void AutoMonitorCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_monitorTimer != null)
            {
                Log("自動監視を開始しました (5秒間隔)。");
                RefreshActiveApps(isAuto: true);
                _monitorTimer.Start();
            }
        }

        private void AutoMonitorCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_monitorTimer != null)
            {
                _monitorTimer.Stop();
                Log("自動監視を停止しました。");
            }
        }

        private void MonitorListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (MonitorListBox.SelectedItem is MonitorItem selectedItem)
            {
                if (selectedItem.Status == "オフライン" || selectedItem.Status == "接続失敗")
                {
                    MessageBox.Show("対象PCがオフラインまたは接続エラーの状態です。", "接続エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string apiKey = ApiKeyTextBox.Text;
                var viewer = new ProcessViewerWindow(selectedItem.PcAddress, apiKey)
                {
                    Owner = this
                };
                viewer.ShowDialog();
            }
        }

        private void IpScanButton_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = ApiKeyTextBox.Text;
            var scanner = new IpScannerWindow(apiKey, httpClient)
            {
                Owner = this
            };

            if (scanner.ShowDialog() == true)
            {
                int count = 0;
                bool regByName = scanner.RegByNameRadio.IsChecked == true;
                string portText = scanner.PortTextBox.Text.Trim();

                foreach (var item in scanner.SelectedItems)
                {
                    string hostString;
                    string machineName = string.Empty;

                    if (regByName && !string.IsNullOrEmpty(item.MachineName) && item.MachineName != "(認証エラー)")
                    {
                        hostString = $"{item.MachineName}:{portText}";
                        machineName = item.MachineName;
                    }
                    else
                    {
                        hostString = item.IpAddress; // "ip:port" 形式
                        if (!string.IsNullOrEmpty(item.MachineName) && item.MachineName != "(名前解決不可)" && item.MachineName != "(認証エラー)")
                        {
                            machineName = item.MachineName;
                        }
                    }

                    if (!PcList.Any(p => p.IpAddress.Equals(hostString, StringComparison.OrdinalIgnoreCase)))
                    {
                        PcList.Add(new PcItem 
                        { 
                            IpAddress = hostString, 
                            MachineName = machineName, 
                            IsSelected = true 
                        });
                        count++;
                    }
                }

                if (count > 0)
                {
                    Log($"スキャン登録完了: {count} 台のPCをリストに追加しました。");
                    SavePcList();
                }
                else
                {
                    Log("スキャン登録完了: 新しく追加されたPCはありません (すべて登録済み)。");
                }
            }
        }
    }

    public class PcItem : INotifyPropertyChanged
    {
        private string _ipAddress = string.Empty;
        private string _machineName = string.Empty;
        private bool _isSelected = true;

        public string IpAddress
        {
            get => _ipAddress;
            set 
            { 
                _ipAddress = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(DisplayName)); 
            }
        }

        public string MachineName
        {
            get => _machineName;
            set 
            { 
                _machineName = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(DisplayName)); 
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(MachineName) || IpAddress.StartsWith(MachineName, StringComparison.OrdinalIgnoreCase))
                {
                    return IpAddress;
                }
                return $"{IpAddress} ({MachineName})";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class UploadResult
    {
        public string FilePath { get; set; } = string.Empty;
    }

    public class MonitorItem : INotifyPropertyChanged
    {
        private string _pcAddress = string.Empty;
        private string _status = "未取得";
        private string _activeApp = string.Empty;
        private System.Windows.Media.Brush _statusColor = System.Windows.Media.Brushes.Gray;

        public string PcAddress
        {
            get => _pcAddress;
            set { _pcAddress = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string ActiveApp
        {
            get => _activeApp;
            set { _activeApp = value; OnPropertyChanged(); }
        }

        public System.Windows.Media.Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class ActiveAppResponse
    {
        public string ActiveApp { get; set; } = string.Empty;
    }
}