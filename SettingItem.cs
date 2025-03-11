using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.LinkOpener
{
    public class SettingItem : INotifyPropertyChanged
    {
        private string keyword = string.Empty;
        private string title = string.Empty;
        private string url = string.Empty;
        private string iconPath = string.Empty;
        private bool addToBulkOpenUrls;
        private string delimiter = "-";

        [JsonPropertyName("Keyword")]
        public string Keyword
        {
            get => keyword;
            set
            {
                if (keyword != value)
                {
                    keyword = value ?? string.Empty;
                    OnPropertyChanged(nameof(Keyword));
                }
            }
        }
        [JsonPropertyName("Title")]
        public string Title
        {
            get => title;
            set
            {
                if (title != value)
                {
                    title = value?.Trim() ?? string.Empty;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }
        [JsonPropertyName("Url")]
        public string Url
        {
            get => url;
            set
            {
                if (url != value)
                {
                    url = value?.Trim() ?? string.Empty;
                    OnPropertyChanged(nameof(Url));
                }
            }
        }

        [JsonPropertyName("Delimiter")]
        public string Delimiter
        {
            get => delimiter;
            set
            {
                if (delimiter != value)
                {
                    delimiter = value ?? "-";
                    OnPropertyChanged(nameof(Delimiter));
                }
            }
        }

        [JsonPropertyName("IconPath")]
        public string IconPath
        {
            get => iconPath;
            set
            {
                if (iconPath != value)
                {
                    iconPath = value?.Trim() ?? string.Empty;
                    OnPropertyChanged(nameof(IconPath));
                }
            }
        }

        [JsonPropertyName("AddToBulkOpenUrls")]
        public bool AddToBulkOpenUrls
        {
            get => addToBulkOpenUrls;
            set
            {
                if (addToBulkOpenUrls != value)
                {
                    addToBulkOpenUrls = value;
                    OnPropertyChanged(nameof(AddToBulkOpenUrls));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Keyword) && !string.IsNullOrWhiteSpace(Url);
        }
        public override string ToString()
        {
            return $"SettingItem: {Keyword} - {Title}";
        }
    }
}
