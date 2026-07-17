using System;
using System.IO;
using System.Net.Http;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace client
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<PcItem> PcList { get; set; }
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private static readonly HttpClient monitoringHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        private readonly ClientDataStore _dataStore = new(AppDomain.CurrentDomain.BaseDirectory);
        private readonly OperationLogger _operationLogger = new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        private readonly RemotePcClient _remotePcClient = new(httpClient, monitoringHttpClient);

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
            LoadConfig();
            UpdateGroupComboBox();
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

            try { _operationLogger.Write(message); }
            catch { }
        }

        private ObservableCollection<PcItem> LoadPcList()
        {
            try
            {
                var list = _dataStore.LoadPcList();
                if (list != null)
                {
                    return new ObservableCollection<PcItem>(list);
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
                _dataStore.SavePcList(PcList);
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

            string group = NewPcGroupTextBox.Text.Trim();

            // 重複チェック
            if (!PcList.Any(p => p.IpAddress.Equals(newPc, StringComparison.OrdinalIgnoreCase)))
            {
                PcList.Add(new PcItem { IpAddress = newPc, Group = group, IsSelected = true });
                Log($"ターゲットPCを追加しました: {newPc}" + (string.IsNullOrEmpty(group) ? "" : $" (グループ: {group})"));
                SavePcList();
                UpdateGroupComboBox();
            }
            NewPcTextBox.Text = "127.0.0.1:5000";
            NewPcGroupTextBox.Text = "";
        }

        private void BulkGenerateButton_Click(object sender, RoutedEventArgs e)
        {
            string prefix = PrefixTextBox.Text.Trim();
            string portText = PortTextBox.Text.Trim();
            string group = BulkPcGroupTextBox.Text.Trim();
            
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

            var generatedHosts = PcNameGenerator.GenerateNames(prefix, startNum, endNum, digits, portText);
            int count = 0;
            foreach (var host in generatedHosts)
            {
                if (!PcList.Any(p => p.IpAddress.Equals(host, StringComparison.OrdinalIgnoreCase)))
                {
                    PcList.Add(new PcItem { IpAddress = host, Group = group, IsSelected = true });
                    count++;
                }
            }

            if (count > 0)
            {
                Log($"一括生成完了: {count} 台のPCをリストに追加しました。" + (string.IsNullOrEmpty(group) ? "" : $" (グループ: {group})"));
                SavePcList();
                UpdateGroupComboBox();
            }
            else
            {
                Log("一括生成完了: 新しく追加されたPCはありません (すべて登録済み)。");
            }
            BulkPcGroupTextBox.Text = "";
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
                UpdateGroupComboBox();
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
                        var result = await _remotePcClient.ExecuteCommandAsync(target.IpAddress, apiKey, command, true);
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
            bool runAsUser = RunAsUserCheckBox.IsChecked == true;
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
                        string remotePath = await _remotePcClient.UploadFileAsync(target.IpAddress, apiKey, localPath);
                        Log($"[{target.IpAddress}] アップロード完了。保存先: {remotePath}");

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
                            else if (ext == ".msix" || ext == ".appx")
                            {
                                // MSIX / APPX パッケージの場合は対話型セッション内でパッケージを登録
                                installCmd = $"Add-AppxPackage -Path \"{remotePath}\"";
                            }
                            else
                            {
                                // exe などの場合は直接実行
                                installCmd = $"Start-Process \"{remotePath}\" -ArgumentList \"{runArgs}\" -Wait -PassThru";
                            }

                            bool runInUserSession = ext is ".msix" or ".appx" || runAsUser;
                            var result = await _remotePcClient.ExecuteCommandAsync(
                                target.IpAddress,
                                apiKey,
                                installCmd,
                                runInUserSession);
                            Log($"[{target.IpAddress}] リモート実行完了。ExitCode: {result.ExitCode}");
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
                        string activeApp = await _remotePcClient.GetActiveAppAsync(target.IpAddress, apiKey);
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
                    catch (RemotePcException ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            item.Status = "接続失敗";
                            item.StatusColor = System.Windows.Media.Brushes.Red;
                            item.ActiveApp = $"HTTP {(int)ex.StatusCode}";
                        });
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
                    UpdateGroupComboBox();
                }
                else
                {
                    Log("スキャン登録完了: 新しく追加されたPCはありません (すべて登録済み)。");
                }
            }
        }

        private void UpdateMachineNamesButton_Click(object sender, RoutedEventArgs e)
        {
            var targets = PcList.Where(p => p.IsSelected).ToList();
            if (!targets.Any())
            {
                Log("警告: PC名更新対象のPCが選択されていません。");
                return;
            }

            string apiKey = ApiKeyTextBox.Text;
            Log($"{targets.Count} 台のPCでPC名の取得を開始します...");

            foreach (var target in targets)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string machineName = await _remotePcClient.GetMachineNameAsync(target.IpAddress, apiKey);
                        if (!string.IsNullOrEmpty(machineName))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                target.MachineName = machineName;
                                Log($"[{target.IpAddress}] PC名を取得しました: {machineName}");
                                SavePcList();
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[{target.IpAddress}] PC名取得中にエラーが発生しました: {ex.Message}");
                    }
                });
            }
        }

        private void EditStudentNameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = PcListBox.SelectedItem as PcItem;
            if (selectedItem == null) return;

            var dialog = new InputDialog($"PC '{selectedItem.IpAddress}' に割り当てる学生名を入力してください:", selectedItem.StudentName)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                selectedItem.StudentName = dialog.InputText.Trim();
                Log($"[{selectedItem.IpAddress}] 学生名を「{selectedItem.StudentName}」に割り当てました。");
                SavePcList();
            }
        }

        private void ClearStudentNameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = PcListBox.SelectedItem as PcItem;
            if (selectedItem == null) return;

            if (MessageBox.Show($"PC '{selectedItem.IpAddress}' の学生名の割り当てを解除しますか？", "割り当て解除の確認", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                selectedItem.StudentName = string.Empty;
                Log($"[{selectedItem.IpAddress}] 学生名の割り当てを解除しました。");
                SavePcList();
            }
        }

        private void StudentMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void UpdateMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void ImportStudentNamesButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSVファイル (*.csv)|*.csv|テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
                Title = "学生名割り当てCSVのインポート"
            };

            if (openFileDialog.ShowDialog() != true) return;

            // Ask if they want to clear existing mappings first
            var clearChoice = MessageBox.Show(
                "インポートする前に、既存の学生名割り当てをすべてクリアしますか？\n\n「はい」：クリアしてからインポート\n「いいえ」：クリアせず上書き・追加",
                "インポート設定",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question
            );

            if (clearChoice == MessageBoxResult.Cancel) return;

            try
            {
                var lines = StudentCsvProcessor.ReadAllLines(openFileDialog.FileName, out string detectedEncoding);

                if (clearChoice == MessageBoxResult.Yes)
                {
                    foreach (var pc in PcList)
                    {
                        pc.StudentName = string.Empty;
                    }
                }

                var parsedRows = StudentCsvProcessor.ParseCsv(lines);
                int successCount = 0;
                int lineCount = lines.Length;

                foreach (var row in parsedRows)
                {
                    var matchedPc = PcList.FirstOrDefault(p => 
                        p.IpAddress.Equals(row.Key, StringComparison.OrdinalIgnoreCase) ||
                        p.IpAddress.StartsWith(row.Key + ":", StringComparison.OrdinalIgnoreCase) ||
                        p.MachineName.Equals(row.Key, StringComparison.OrdinalIgnoreCase)
                    );

                    if (matchedPc != null)
                    {
                        matchedPc.StudentName = row.StudentName;
                        successCount++;
                    }
                }

                SavePcList();
                Log($"CSVインポート完了 ({detectedEncoding}): {lineCount}行中 {successCount}台のPCに学生名を割り当てました。");
                MessageBox.Show($"インポートが完了しました。\n\n割り当て成功: {successCount} 台", "インポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"CSVインポートエラー: {ex.Message}");
                MessageBox.Show($"CSVのインポート中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportStudentNamesButton_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSVファイル (*.csv)|*.csv|テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
                Title = "学生名割り当てCSVのエクスポート",
                FileName = "student_pc_mapping.csv"
            };

            if (saveFileDialog.ShowDialog() != true) return;

            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                var encoding = System.Text.Encoding.GetEncoding(932);

                var csvLines = new System.Collections.Generic.List<string>
                {
                    "キー(IPまたはPC名),学生名,IPアドレス,PC名,グループ"
                };

                foreach (var pc in PcList)
                {
                    string key = !string.IsNullOrEmpty(pc.MachineName) ? pc.MachineName : pc.IpAddress;
                    string student = pc.StudentName;

                    string csvRow = StudentCsvProcessor.BuildCsvRow(key, student, pc.IpAddress, pc.MachineName, pc.Group);
                    csvLines.Add(csvRow);
                }

                File.WriteAllLines(saveFileDialog.FileName, csvLines, encoding);
                Log($"CSVエクスポート完了: {saveFileDialog.FileName} に書き出しました。");
                MessageBox.Show("エクスポートが完了しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"CSVエクスポートエラー: {ex.Message}");
                MessageBox.Show($"CSVのエクスポート中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateMacAddressesButton_Click(object sender, RoutedEventArgs e)
        {
            var targets = PcList.Where(p => p.IsSelected).ToList();
            if (!targets.Any())
            {
                Log("警告: MACアドレス更新対象のPCが選択されていません。");
                return;
            }

            string apiKey = ApiKeyTextBox.Text;
            Log($"{targets.Count} 台のPCでMACアドレスの取得を開始します...");

            foreach (var target in targets)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string macAddress = await _remotePcClient.GetMacAddressAsync(target.IpAddress, apiKey);
                        if (!string.IsNullOrEmpty(macAddress))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                target.MacAddress = macAddress;
                                Log($"[{target.IpAddress}] MACアドレスを取得しました: {macAddress}");
                                SavePcList();
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[{target.IpAddress}] MACアドレス取得中にエラーが発生しました: {ex.Message}");
                    }
                });
            }
        }

        private async void WolBootButton_Click(object sender, RoutedEventArgs e)
        {
            var targets = PcList.Where(p => p.IsSelected).ToList();
            if (!targets.Any())
            {
                Log("警告: WOL起動対象のPCが選択されていません。");
                return;
            }

            int sendCount = 0;

            foreach (var target in targets)
            {
                if (string.IsNullOrEmpty(target.MacAddress))
                {
                    Log($"警告: [{target.IpAddress}] はMACアドレスが登録されていないため、WOL起動をスキップします。先に「MAC更新」を行ってください。");
                    continue;
                }

                try
                {
                    await WakeOnLanService.SendAsync(target.MacAddress);
                    Log($"[{target.IpAddress}] へWOL起動シグナル（Magic Packet）を送信しました。");
                    sendCount++;
                }
                catch (Exception ex)
                {
                    Log($"[{target.IpAddress}] WOL送信エラー: {ex.Message}");
                }
            }

            if (sendCount > 0)
            {
                Log($"WOL起動完了: {sendCount} 台のPCへ起動信号を送信しました。");
            }
        }

        private void LoadConfig()
        {
            try
            {
                var config = _dataStore.LoadConfig();
                if (config != null && !string.IsNullOrEmpty(config.ApiKey))
                {
                    ApiKeyTextBox.Text = config.ApiKey;
                }
                else
                {
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                Log($"設定ファイルの読み込み中にエラーが発生しました: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                var config = new ClientConfig
                {
                    ApiKey = ApiKeyTextBox?.Text ?? ""
                };
                _dataStore.SaveConfig(config);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定ファイルの保存中にエラーが発生しました: {ex.Message}");
            }
        }

        private void ApiKeyTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SaveConfig();
        }

        private bool _isUpdatingGroupComboBox = false;

        private void UpdateGroupComboBox()
        {
            if (GroupFilterComboBox == null) return;

            _isUpdatingGroupComboBox = true;
            try
            {
                string? selectedGroup = GroupFilterComboBox.SelectedItem as string;

                GroupFilterComboBox.Items.Clear();
                GroupFilterComboBox.Items.Add("（選択してください）");
                GroupFilterComboBox.Items.Add("（グループ未設定）");

                var groups = PcList
                    .Where(p => !string.IsNullOrEmpty(p.Group))
                    .Select(p => p.Group)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g)
                    .ToList();

                foreach (var g in groups)
                {
                    GroupFilterComboBox.Items.Add(g);
                }

                if (selectedGroup != null && GroupFilterComboBox.Items.Contains(selectedGroup))
                {
                    GroupFilterComboBox.SelectedItem = selectedGroup;
                }
                else
                {
                    GroupFilterComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _isUpdatingGroupComboBox = false;
            }
        }

        private void GroupFilterComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isUpdatingGroupComboBox) return;

            string? selectedGroup = GroupFilterComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedGroup) || selectedGroup == "（選択してください）")
            {
                return;
            }

            foreach (var pc in PcList)
            {
                if (selectedGroup == "（グループ未設定）")
                {
                    pc.IsSelected = string.IsNullOrEmpty(pc.Group);
                }
                else
                {
                    pc.IsSelected = pc.Group.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase);
                }
            }

            Log($"グループ「{selectedGroup}」のPCのみを選択しました。");
        }
    }

}
