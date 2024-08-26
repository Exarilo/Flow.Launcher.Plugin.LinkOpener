using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;
using Flow.Launcher.Plugin;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.LinkOpener
{
    public class LinkOpener : IPlugin, ISettingProvider
    {
        private ObservableCollection<SettingItem> settingsItems;
        internal PluginInitContext Context;

        string SettingsFolder => Path.Combine(
             Path.GetDirectoryName(Path.GetDirectoryName(Context.CurrentPluginMetadata.PluginDirectory)),
             "Settings",
             "Plugins",
             "Flow.Launcher.Plugin.LinkOpener"
         );
        string SettingsPath => Path.Combine(SettingsFolder, "Settings.json");

        public void Init(PluginInitContext context)
        {
            Context = context;
            LoadSettings().GetAwaiter().GetResult();
        }

        private async Task LoadSettings()
        {
            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
            }
            if (File.Exists(SettingsPath))
            {
                string jsonData = await File.ReadAllTextAsync(SettingsPath);
                settingsItems = JsonSerializer.Deserialize<ObservableCollection<SettingItem>>(jsonData) ?? new ObservableCollection<SettingItem>();
            }
            else
            {
                settingsItems = new ObservableCollection<SettingItem>();
            }
        }

        public List<Result> Query(Query query)
        {
            var results = new List<Result>();
            string keyword = query.FirstSearch.Trim().ToLower();

            string arg = query.SecondSearch?.Trim();

            var filteredItems = settingsItems
                .Where(item => keyword.StartsWith(item.Keyword.Trim(), StringComparison.OrdinalIgnoreCase) && (string.IsNullOrEmpty(arg) || item.Title.Contains(arg, StringComparison.OrdinalIgnoreCase)));

            foreach (var item in filteredItems)
            {
                results.Add(CreateResult(item));
            }

            return results;
        }

        private Result CreateResult(SettingItem settingItem)
        {
            if (!Uri.TryCreate(settingItem.Url, UriKind.Absolute, out Uri uri))
                return new Result();

            return new Result
            {
                Title = settingItem.Title,
                SubTitle = $"Link to {settingItem.Title}",
                Action = e =>
                {
                    Context.API.OpenUrl(settingItem.Url);
                    return true;
                },
                IcoPath = settingItem.IconPath?? "Images\\app.png" 
            };
        }

        public Control CreateSettingPanel()
        {
            return new LinkSettings(settingsItems,Context);
        }
    }
}
