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
        private const int BASE_SCORE = 1000;
        private const int BULK_SCORE = 10000;
        private const int ARG_MATCH_BONUS = 500;

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

            if (args.Any())
            {
                foreach (var result in filteredResults)
                {
                    result.Score = CalculateScore((string)result.ContextData, args);
                }
            }

            if (filteredItemsToBulkOpen.Count > 1)
            {
                var bulkResult = CreateBulkOpenResult(fullSearch, filteredItemsToBulkOpen, args);
                if (args.Any())
                {
                    int totalPlaceholders = filteredItemsToBulkOpen.Sum(item => UrlUpdater.CountPlaceholdersUsed(item.Url));
                    bulkResult.Score = BULK_SCORE + (ARG_MATCH_BONUS * totalPlaceholders);
                }
                filteredResults.Add(bulkResult);
            }

            return filteredResults;
        }

        private int CalculateScore(string url, List<string> args)
        {
            int score = BASE_SCORE;
            if (!args.Any()) 
                return score;

            int argsUsed = UrlUpdater.CountPlaceholdersUsed(url);

            if (argsUsed > 0)
            {
                score += ARG_MATCH_BONUS * argsUsed;
            }

            return score;
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

        public async Task<Result> CreateResult(SettingItem settingItem, List<string> args)
        {
            Uri updatedUri = UrlUpdater.UpdateUrl(settingItem.Url, args);
            if (updatedUri == null)
                return null;

            Uri faviconUri = new Uri($"https://www.google.com/s2/favicons?domain_url={updatedUri.Host}&sz=48");
            string iconPath;

            if (string.IsNullOrEmpty(settingItem.IconPath))
                iconPath = await IsFaviconAccessible(faviconUri) ? faviconUri.ToString() : "Images\\app.png";
            else
                iconPath = settingItem.IconPath;

            return new Result
            {
                Title = settingItem.Title,
                SubTitle = updatedUri.ToString(),
                Score = BASE_SCORE,
                Action = e =>
                {
                    Context.API.OpenUrl(updatedUri);
                    return true;
                },
                IcoPath = iconPath,
                ContextData = settingItem.Url
            };
        }

        private static async Task<bool> IsFaviconAccessible(Uri faviconUri)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var response = await client.GetAsync(faviconUri);
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
       
        [GeneratedRegex(@"\{\d+\}", RegexOptions.None, matchTimeoutMilliseconds: 3000)]
        private static partial Regex PlaceholderCountRegex();

        public static int CountPlaceholdersUsed(string url)
        {
            return PlaceholderCountRegex().Matches(url).Count;
        }
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
            return null;
        }
    }
}