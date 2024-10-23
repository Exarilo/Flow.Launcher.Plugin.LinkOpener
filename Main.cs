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

            if (filteredItemsToBulkOpen.Count > 1)
            {
                results.Add(CreateBulkOpenResult(fullSearch, filteredItemsToBulkOpen, args));
            }

            return results;
        }

        private static bool MatchesSearch(SettingItem item, string fullSearch)
        {
            string searchKeyword = item.Keyword.Trim().ToLower();
            if (!fullSearch.StartsWith(searchKeyword, StringComparison.OrdinalIgnoreCase))
                return false;

            string remainingSearch = fullSearch.Substring(searchKeyword.Length).Trim();

            List<string> args = remainingSearch.Split(' ').ToList();
            return string.IsNullOrEmpty(remainingSearch) || args.TrueForAll(arg => item.Title.ToLower().Contains(arg));
        }

        private static List<string> GetAndRemoveArgs(ref string query)
        {
            List<string> args = new List<string>();
            string pattern = @"-\s*([^-]+)";

            TimeSpan timeout = TimeSpan.FromSeconds(3);
            MatchCollection matches = Regex.Matches(query.Trim(), pattern, RegexOptions.None, timeout);

            foreach (Match match in matches)
            {
                string arg = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(arg))
                {
                    args.Add(arg);
                }
            }

            query = Regex.Replace(query, pattern, "", RegexOptions.None, timeout).Trim();

            return args;
        }

        private async Task<Result> CreateResult(SettingItem settingItem, List<string> args)
        {
            string updatedUrl = UrlUpdater.UpdateUrl(settingItem.Url, args);

            if (!Uri.TryCreate(updatedUrl, UriKind.Absolute, out Uri uri))
                return null;

            string faviconUrl = $"https://www.google.com/s2/favicons?domain_url={uri.Host}&sz=48";
            string iconPath;
            if (string.IsNullOrEmpty(settingItem.IconPath))
                iconPath = await IsFaviconAccessible(faviconUrl) ? faviconUrl : "Images\\app.png";
            else
                iconPath = settingItem.IconPath;

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

        private static async Task<bool> IsFaviconAccessible(string faviconUrl)
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
                        string updatedUrl = UrlUpdater.UpdateUrl(item.Url, args);
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

        public Control CreateSettingPanel()
        {
            return new LinkSettings(settingsItems, Context);
        }
    }

    public static partial class UrlUpdater 
    {
        [GeneratedRegex(@"\{(\d+)\}", RegexOptions.None, matchTimeoutMilliseconds: 3000)]
        private static partial Regex PlaceholderRegex();

        [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 3000)]
        private static partial Regex WhitespaceRegex();

        public static string UpdateUrl(string url, List<string> args)
        {
            string updatedUrl = PlaceholderRegex().Replace(url, match =>
            {
                if (int.TryParse(match.Groups[1].Value, out int index) && index >= 0 && index < args.Count)
                {
                    return Uri.EscapeDataString(args[index]);
                }
                return string.Empty;
            });

            return WhitespaceRegex().Replace(updatedUrl.Trim(), " ");
        }
    }
}