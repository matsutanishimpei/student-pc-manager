using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace client
{
    public class PcItem : INotifyPropertyChanged
    {
        private string _ipAddress = string.Empty;
        private string _machineName = string.Empty;
        private string _macAddress = string.Empty;
        private string _group = string.Empty;
        private string _studentName = string.Empty;
        private bool _isSelected = true;

        public string IpAddress { get => _ipAddress; set => SetField(ref _ipAddress, value, true); }
        public string MachineName { get => _machineName; set => SetField(ref _machineName, value, true); }
        public string MacAddress { get => _macAddress; set => SetField(ref _macAddress, value, true); }
        public string Group { get => _group; set => SetField(ref _group, value, true); }
        public string StudentName { get => _studentName; set => SetField(ref _studentName, value, true); }
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                string baseName = !string.IsNullOrEmpty(StudentName)
                    ? $"{StudentName} ({IpAddress})"
                    : !string.IsNullOrEmpty(MachineName) && !IpAddress.StartsWith(MachineName, StringComparison.OrdinalIgnoreCase)
                        ? $"{IpAddress} ({MachineName})"
                        : IpAddress;

                if (!string.IsNullOrEmpty(Group))
                {
                    baseName = $"[{Group}] {baseName}";
                }

                return string.IsNullOrEmpty(MacAddress) ? baseName : $"{baseName} <{MacAddress}>";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, bool affectsDisplayName = false, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (affectsDisplayName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
            }
        }
    }

    public class MonitorItem : INotifyPropertyChanged
    {
        private string _pcAddress = string.Empty;
        private string _status = "未取得";
        private string _activeApp = string.Empty;
        private Brush _statusColor = Brushes.Gray;

        public string PcAddress { get => _pcAddress; set => SetField(ref _pcAddress, value); }
        public string Status { get => _status; set => SetField(ref _status, value); }
        public string ActiveApp { get => _activeApp; set => SetField(ref _activeApp, value); }
        public Brush StatusColor { get => _statusColor; set => SetField(ref _statusColor, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class UploadResult
    {
        public string FilePath { get; set; } = string.Empty;
    }

    public class ActiveAppResponse
    {
        public string ActiveApp { get; set; } = string.Empty;
    }

    public class ClientConfig
    {
        public string ApiKey { get; set; } = string.Empty;
    }
}
