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
        private const string SETTINGS_FILENAME = "Settings.json";
        private const string FAVICON_URL_TEMPLATE = "https://www.google.com/s2/favicons?domain_url={0}&sz=48";
        private const string DEFAULT_ICON_PATH = "Images\\app.png";
        private static readonly HttpClient _httpClient = new HttpClient();

        private string SettingsFolder => Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(Context.CurrentPluginMetadata.PluginDirectory)),
            "Settings",
            "Plugins",
            "Flow.Launcher.Plugin.LinkOpener"
        );
        private string SettingsPath => Path.Combine(SettingsFolder, SETTINGS_FILENAME);

        public async Task InitAsync(PluginInitContext context)
        {
            Context = context;
            await LoadSettings().ConfigureAwait(false);
        }

        private async Task LoadSettings()
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }

                if (File.Exists(SettingsPath))
                {
                    string jsonData = await File.ReadAllTextAsync(SettingsPath).ConfigureAwait(false);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    settingsItems = JsonSerializer.Deserialize<ObservableCollection<SettingItem>>(jsonData, options) ?? new ObservableCollection<SettingItem>();
                }
                else
                {
                    settingsItems = new ObservableCollection<SettingItem>();
                }
            }
            catch
            {
                settingsItems = new ObservableCollection<SettingItem>();
            }
        }

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(query.Search))
                return new List<Result>();

            string fullSearch = query.Search.Trim().ToLower();

            var matchingItems = settingsItems.AsParallel()
                .Where(item => MatchesSearch(item, fullSearch))
                .ToList();

            if (token.IsCancellationRequested || !matchingItems.Any())
                return new List<Result>();

            var results = new List<Result>();

            foreach (var item in matchingItems)
            {
                try
                {
                    string searchCopy = fullSearch;
                    List<string> args = GetAndRemoveArgs(ref searchCopy, item);
                    var result = await CreateResult(item, args);
                    if (result != null)
                    {
                        results.Add(result);
                        result.Score = CalculateScore((string)result.ContextData, args);
                    }
                }
                catch { }
            }

            if (token.IsCancellationRequested)
                return new List<Result>();
            try
            {
                var itemsToBulkOpen = matchingItems.Where(x => x.AddToBulkOpenUrls).ToList();
                if (itemsToBulkOpen.Count > 1)
                {
                    string searchCopy = fullSearch;
                    List<string> args = GetAndRemoveArgs(ref searchCopy, itemsToBulkOpen.First());

                    var bulkResult = CreateBulkOpenResult(fullSearch, itemsToBulkOpen, args);
                    if (args.Any())
                    {
                        int totalPlaceholders = itemsToBulkOpen.Sum(item => UrlUpdater.CountPlaceholdersUsed(item.Url));
                        bulkResult.Score = BULK_SCORE + (ARG_MATCH_BONUS * totalPlaceholders);
                    }
                    results.Add(bulkResult);
                }
            }
            catch { }

            return results;
        }

        private static int CalculateScore(string url, List<string> args)
        {
            if (string.IsNullOrEmpty(url) || !args.Any())
                return BASE_SCORE;

            int argsUsed = UrlUpdater.CountPlaceholdersUsed(url);
            return argsUsed > 0 ? BASE_SCORE + (ARG_MATCH_BONUS * argsUsed) : BASE_SCORE;
        }

        private static bool MatchesSearch(SettingItem item, string fullSearch)
        {
            if (item == null || string.IsNullOrEmpty(item.Keyword) || string.IsNullOrEmpty(fullSearch))
                return false;

            string searchKeyword = item.Keyword.Trim().ToLower();
            string delimiter = string.IsNullOrWhiteSpace(item.Delimiter) ? " " : item.Delimiter;

            if (searchKeyword.Contains(" "))
            {
                return fullSearch.TrimStart().ToLower().StartsWith(searchKeyword);
            }

            string[] parts = delimiter == " "
                ? fullSearch.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                : fullSearch.Split(new[] { delimiter }, StringSplitOptions.None);

            if (parts.Length == 0) return false;

            string searchText = parts[0].Trim().ToLower();
            return searchText.Equals(searchKeyword, StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> GetAndRemoveArgs(ref string query, SettingItem item)
        {
            List<string> args = new List<string>();
            try
            {
                string delimiter = string.IsNullOrWhiteSpace(item?.Delimiter) ? " " : item.Delimiter;
                string keyword = item?.Keyword?.Trim().ToLower() ?? "";

                if (keyword.Contains(" "))
                {
                    if (query.Trim().ToLower().StartsWith(keyword))
                    {
                        string remainingText = query.Substring(keyword.Length).Trim();
                        if (!string.IsNullOrWhiteSpace(remainingText))
                        {
                            if (delimiter == " ")
                            {
                                string[] parts = remainingText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                args.AddRange(parts);
                            }
                            else
                            {
                                string pattern = $"{Regex.Escape(delimiter)}\\s*([^{Regex.Escape(delimiter)}]+)";
                                TimeSpan timeout = TimeSpan.FromSeconds(3);

                                MatchCollection matches = Regex.Matches(remainingText, pattern, RegexOptions.None, timeout);
                                foreach (Match match in matches)
                                {
                                    string arg = match.Groups[1].Value.Trim();
                                    if (!string.IsNullOrWhiteSpace(arg))
                                    {
                                        args.Add(arg);
                                    }
                                }
                            }
                        }

                        query = keyword;
                        return args;
                    }
                }

                if (delimiter == " ")
                {
                    string[] parts = query.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        args.AddRange(parts.Skip(1));
                        query = parts[0];
                    }
                }
                else
                {
                    string pattern = $"{Regex.Escape(delimiter)}\\s*([^{Regex.Escape(delimiter)}]+)";
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
                }
            }
            catch { }

            return args;
        }

        public async Task<Result> CreateResult(SettingItem settingItem, IEnumerable<string> args)
        {
            if (settingItem == null || string.IsNullOrEmpty(settingItem.Url))
                return null;

            Uri updatedUri = UrlUpdater.UpdateUrl(settingItem.Url, args.ToList());
            if (updatedUri == null)
                return null;

            string iconPath;

            if (string.IsNullOrEmpty(settingItem.IconPath))
            {
                Uri faviconUri = new Uri(string.Format(FAVICON_URL_TEMPLATE, updatedUri.Host));
                iconPath = await IsFaviconAccessible(faviconUri).ConfigureAwait(false)
                    ? faviconUri.ToString()
                    : DEFAULT_ICON_PATH;
            }
            else
            {
                iconPath = settingItem.IconPath;
            }

            return new Result
            {
                Title = settingItem.Title,
                SubTitle = updatedUri.ToString(),
                Score = BASE_SCORE,
                Action = e =>
                {
                    try
                    {
                        Context.API.OpenUrl(updatedUri);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                },
                IcoPath = iconPath,
                ContextData = settingItem.Url
            };
        }

        private static async Task<bool> IsFaviconAccessible(Uri faviconUri)
        {
            try
            {
                var response = await _httpClient.GetAsync(faviconUri).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
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
                Score = BULK_SCORE,
                Action = e =>
                {
                    try
                    {
                        int successCount = 0;
                        foreach (var item in items)
                        {
                            Uri updatedUri = UrlUpdater.UpdateUrl(item.Url, args);
                            if (updatedUri != null)
                            {
                                Context.API.OpenUrl(updatedUri);
                                successCount++;
                            }
                        }

                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                },
                IcoPath = DEFAULT_ICON_PATH
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
            if (string.IsNullOrEmpty(url))
                return 0;

            return PlaceholderCountRegex().Matches(url).Count;
        }

        public static Uri UpdateUrl(string url, IEnumerable<string> args)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            try
            {
                var argsList = args.ToList();
                string updatedUrl = PlaceholderRegex().Replace(url, match =>
                {
                    if (int.TryParse(match.Groups[1].Value, out int index) && index >= 0 && index < argsList.Count)
                    {
                        return Uri.EscapeDataString(argsList[index]);
                    }
                    return string.Empty;
                });

                updatedUrl = WhitespaceRegex().Replace(updatedUrl.Trim(), " ");
                if (Uri.TryCreate(updatedUrl, UriKind.Absolute, out Uri uri))
                {
                    return uri;
                }
            }
            catch { }

            return null;
        }
    }
}