using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.LinkOpener
{
    public class LinkOpener : IPlugin, ISettingProvider
    {
        private ObservableCollection<SettingItem> settingsItems;
        internal PluginInitContext Context;

        private string SettingsFolder => Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(Context.CurrentPluginMetadata.PluginDirectory)),
            "Settings",
            "Plugins",
            "Flow.Launcher.Plugin.LinkOpener"
        );
        private string SettingsPath => Path.Combine(SettingsFolder, "Settings.json");

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
            List<string> args = GetAndRemoveArgs(ref fullSearch);

            var filteredItems = settingsItems.Where(item => MatchesSearch(item, fullSearch));
            var filteredItemsToBulkOpen = filteredItems.Where(x => x.AddToBulkOpenUrls);

            var results = new List<Result>();
            results.AddRange(filteredItems.Select(x => CreateResult(x, args)).Where(result => result != null));

            if (filteredItemsToBulkOpen.Count() > 1 && results.Count > 1)
            {
                results.Add(CreateBulkOpenResult(query.FirstSearch.Trim(), filteredItemsToBulkOpen, args));
            }

            return results;
        }

        private bool MatchesSearch(SettingItem item, string fullSearch)
        {
            if (!fullSearch.StartsWith(item.Keyword.Trim().ToLower(), StringComparison.OrdinalIgnoreCase))
                return false;

            string remainingSearch = fullSearch.Substring(item.Keyword.Trim().Length).Trim();

            if (string.IsNullOrEmpty(remainingSearch))
                return true;

            return remainingSearch.Split(' ')
                .All(arg => item.Title.ToLower().Contains(arg));
        }

        private List<string> GetAndRemoveArgs(ref string query)
        {
            List<string> args = new List<string>();
            string pattern = @"-\s*(\w+)";
            MatchCollection matches = Regex.Matches(query, pattern);

            foreach (Match match in matches)
            {
                args.Add(match.Groups[1].Value);
            }

            query = Regex.Replace(query, pattern, "").Trim().Replace('-', ' ');

            return args;
        }

        private Result CreateResult(SettingItem settingItem, List<string> args)
        {
            string updatedUrl = UpdateUrl(settingItem.Url, args);

            if (!Uri.TryCreate(updatedUrl, UriKind.Absolute, out Uri uri))
                return null;

            return new Result
            {
                Title = settingItem.Title,
                SubTitle = $"{updatedUrl}",
                Score = 1000,
                Action = e =>
                {
                    Context.API.OpenUrl(updatedUrl);
                    return true;
                },
                IcoPath = string.IsNullOrEmpty(settingItem.IconPath) ? "Images\\app.png" : settingItem.IconPath
            };
        }

        private Result CreateBulkOpenResult(string searchTerm, IEnumerable<SettingItem> itemsToOpen, List<string> args)
        {
            return new Result
            {
                Title = $@"Bulk Open ""{searchTerm}""",
                SubTitle = "Open all links",
                Score = 10000,
                Action = e =>
                {
                    foreach (var item in itemsToOpen)
                    {
                        string updatedUrl = UpdateUrl(item.Url, args);
                        if (Uri.TryCreate(updatedUrl, UriKind.Absolute, out Uri uri))
                        {
                            Context.API.OpenUrl(updatedUrl);
                        }
                    }
                    return true;
                },
                IcoPath = "Images\\app.png"
            };
        }

        private string UpdateUrl(string url, List<string> args)
        {
            string updatedUrl = Regex.Replace(url, @"\{(\d+)\}", match =>
            {
                if (int.TryParse(match.Groups[1].Value, out int index) && index >= 0 && index < args.Count)
                {
                    return Uri.EscapeDataString(args[index]);
                }
                return string.Empty;
            });

            return Regex.Replace(updatedUrl.Trim(), @"\s+", " ");
        }

        public Control CreateSettingPanel()
        {
            return new LinkSettings(settingsItems, Context);
        }
    }
}