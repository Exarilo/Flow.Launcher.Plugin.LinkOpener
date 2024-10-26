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
using System.Threading;

namespace Flow.Launcher.Plugin.LinkOpener
{
    public class LinkOpener : IAsyncPlugin, ISettingProvider
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

        public async Task InitAsync(PluginInitContext context)
        {
            Context = context;
            await LoadSettings();
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

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            string fullSearch = query.Search.Trim().ToLower();
            List<string> args = GetAndRemoveArgs(ref fullSearch);

            var filteredItems = settingsItems.Where(item => MatchesSearch(item, fullSearch));
            var filteredItemsToBulkOpen = filteredItems.Where(x => x.AddToBulkOpenUrls).ToList();

            var results = await Task.WhenAll(filteredItems.Select(x => CreateResult(x, args)));

            var filteredResults = results.Where(result => result != null).ToList();

            if (filteredItemsToBulkOpen.Count > 1)
            {
                filteredResults.Add(CreateBulkOpenResult(fullSearch, filteredItemsToBulkOpen, args));
            }

            return filteredResults;
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
            const string pattern = @"-\s*([^-]+)";

            List<string> args = new List<string>();
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
            Uri updatedUri = UrlUpdater.UpdateUrl(settingItem.Url, args);

            if (updatedUri == null)
                return null;

            string faviconUrl = $"https://www.google.com/s2/favicons?domain_url={updatedUri.Host}&sz=48";
            string iconPath;

            if (string.IsNullOrEmpty(settingItem.IconPath))
                iconPath = await IsFaviconAccessible(faviconUrl) ? faviconUrl : "Images\\app.png";
            else
                iconPath = settingItem.IconPath;

            return new Result
            {
                Title = settingItem.Title,
                SubTitle = updatedUri.ToString(),
                Score = 1000,
                Action = e =>
                {
                    Context.API.OpenUrl(updatedUri.ToString()); // Convert Uri to string
                    return true;
                },
                IcoPath = iconPath
            };
        }

        private async Task<bool> IsFaviconAccessible(string faviconUrl) // Change parameter type to string
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
                        Uri updatedUri = UrlUpdater.UpdateUrl(item.Url, args);
                        if (updatedUri != null) 
                        {
                            Context.API.OpenUrl(updatedUri); 
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

        public static Uri UpdateUrl(string url, List<string> args)
        {
            string updatedUrl = PlaceholderRegex().Replace(url, match =>
            {
                if (int.TryParse(match.Groups[1].Value, out int index) && index >= 0 && index < args.Count)
                {
                    return Uri.EscapeDataString(args[index]);
                }
                return string.Empty;
            });
            updatedUrl = WhitespaceRegex().Replace(updatedUrl.Trim(), " ");
            if (Uri.TryCreate(updatedUrl, UriKind.Absolute, out Uri uri))
            {
                return uri;
            }

            throw new UriFormatException($"The URL '{updatedUrl}' is not valid.");
        }
    }
}