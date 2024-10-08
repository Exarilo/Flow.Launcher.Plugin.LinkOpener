using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.LinkOpener
{
    public class SettingItem : INotifyPropertyChanged
    {
        private string keyword;
        private string title;
        private string url;
        private string iconPath;
        private bool addToBulkOpenUrls;

        [JsonPropertyName("Keyword")]
        public string Keyword
        {
            get => keyword;
            set
            {
                if (keyword != value)
                {
                    keyword = value;
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
                    title = value;
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
                    url = value;
                    OnPropertyChanged(nameof(Url));
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
                    iconPath = value;
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
    }
}
