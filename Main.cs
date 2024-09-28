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
            string fullSearch = query.Search.Trim().ToLower();

            var filteredItems = settingsItems
                .Where(item =>
                {
                    if (!fullSearch.StartsWith(item.Keyword.Trim().ToLower(), StringComparison.OrdinalIgnoreCase))
                        return false;

                    string remainingSearch = fullSearch.Substring(item.Keyword.Trim().Length).Trim();

                    if (string.IsNullOrEmpty(remainingSearch))
                        return true;

                    return remainingSearch.Split(' ')
                        .All(arg => item.Title.ToLower().Contains(arg));
                });

            var filteredItemsToBulkOpen = filteredItems.Where(x => x.AddToBulkOpenUrls);

            var results = new List<Result>();

            results.AddRange(filteredItems.Select(CreateResult));
            if (filteredItemsToBulkOpen.Count() > 1)
            {
                results.Add(new Result
                {
                    Title = $@"Bulk Open ""{query.FirstSearch.Trim()}""",
                    SubTitle = "Open all links",
                    Score = 10000,
                    Action = e =>
                    {
                        filteredItemsToBulkOpen.ToList().ForEach(x => Context.API.OpenUrl(x.Url));
                        return true;
                    },
                    IcoPath = "Images\\app.png"
                });
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
                SubTitle = $"{settingItem.Url}",
                Score = 1000,
                Action = e =>
                {
                    Context.API.OpenUrl(settingItem.Url);
                    return true;
                },
                IcoPath = settingItem.IconPath ?? "Images\\app.png"
            };
        }

        public Control CreateSettingPanel()
        {
            return new LinkSettings(settingsItems, Context);
        }
    }
}
