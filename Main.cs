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
using System.Net.Http;

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
            var filteredItemsToBulkOpen = filteredItems.Where(x => x.AddToBulkOpenUrls).ToList();

            var results = filteredItems.Select(x => CreateResult(x, args).Result)
                                      .Where(result => result != null)
                                      .ToList();

            if (filteredItemsToBulkOpen.Any())
            {
                results.Add(CreateBulkOpenResult(fullSearch, filteredItemsToBulkOpen, args));
            }

            return results;
        }

        private bool MatchesSearch(SettingItem item, string fullSearch)
        {
            string searchKeyword = item.Keyword.Trim().ToLower();

            string searchBeforeDash = fullSearch.Split('-')[0].Trim().ToLower();

            return searchBeforeDash.StartsWith(searchKeyword, StringComparison.OrdinalIgnoreCase);
        }

        private List<string> GetAndRemoveArgs(ref string query)
        {
            List<string> args = new List<string>();
            string pattern = @"-\s*([^-]+)";

            MatchCollection matches = Regex.Matches(query.Trim(), pattern);

            foreach (Match match in matches)
            {
                 string arg = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(arg))
                {
                    args.Add(arg);
                }
            }

      
            query = Regex.Replace(query, pattern, "").Trim();

            return args;
        }

        private async Task<Result> CreateResult(SettingItem settingItem, List<string> args)
        {
            string updatedUrl = UpdateUrl(settingItem.Url, args);

            if (!Uri.TryCreate(updatedUrl, UriKind.Absolute, out Uri uri))
                return null;

            string faviconUrl = $"https://www.google.com/s2/favicons?domain_url={uri.Host}&sz=48";
            string iconPath = string.IsNullOrEmpty(settingItem.IconPath) ?
                (await IsFaviconAccessible(faviconUrl) ? faviconUrl : "Images\\app.png") : settingItem.IconPath;

            return new Result
            {
                Title = settingItem.Title,
                SubTitle = updatedUrl,
                Score = 1000,
                Action = e =>
                {
                    Context.API.OpenUrl(updatedUrl);
                    return true;
                },
                IcoPath = iconPath
            };
        }

        private async Task<bool> IsFaviconAccessible(string faviconUrl)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var response = await client.GetAsync(faviconUrl);
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private Result CreateBulkOpenResult(string cleanSearchTerm, IEnumerable<SettingItem> itemsToOpen, List<string> args)
        {
            var items = itemsToOpen.ToList();
            var argsDisplay = args.Any() ? $" with args: {string.Join(", ", args)}" : "";

            return new Result
            {
                Title = $"Bulk Open ({items.Count} items){argsDisplay}",
                SubTitle = $"Open all matching links for '{cleanSearchTerm}'",
                Score = 10000,
                Action = e =>
                {
                    foreach (var item in items)
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