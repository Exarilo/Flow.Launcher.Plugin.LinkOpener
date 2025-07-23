using Quickenshtein;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Flow.Launcher.Plugin.LinkOpener
{
    public enum SettingMatchType
    {
        NoPlaceholder,
        PlaceholderWithoutDelimiter,
        PlaceholderWithDelimiter
    }

    public class LinkOpener : IAsyncPlugin, ISettingProvider
    {
        private ObservableCollection<SettingItem> settingsItems;
        internal PluginInitContext Context;
        private const int BASE_SCORE = 1000;
        private const int BULK_SCORE = 10000;
        private const int SIMILARITY_MULTIPLIER = 50;
        private const int BULK_SCORE_BONUS = 5000;
        private const string SETTINGS_FILENAME = "Settings.json";
        private const string FAVICON_URL_TEMPLATE = "https://www.google.com/s2/favicons?domain_url={0}&sz=48";
        private const string DEFAULT_ICON_PATH = "Images\\app.png";
        private static readonly HttpClient _httpClient = new HttpClient{Timeout = TimeSpan.FromSeconds(5)};
        private static readonly ConcurrentDictionary<string, string> _faviconCache = new();
        private string _settingsFolder;
        private string _settingsPath;

        private string SettingsFolder => _settingsFolder ??= Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(Context.CurrentPluginMetadata.PluginDirectory)),
            "Settings",
            "Plugins",
            "Flow.Launcher.Plugin.LinkOpener"
        );

        private string SettingsPath => _settingsPath ??= Path.Combine(SettingsFolder, SETTINGS_FILENAME);

        public async Task InitAsync(PluginInitContext context)
        {
            Context = context;
            await LoadSettingsAsync().ConfigureAwait(false);
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }

                if (!File.Exists(SettingsPath))
                {
                    settingsItems = new ObservableCollection<SettingItem>();
                    return;
                }

                var jsonData = await File.ReadAllTextAsync(SettingsPath).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(jsonData))
                {
                    settingsItems = new ObservableCollection<SettingItem>();
                    return;
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                settingsItems = JsonSerializer.Deserialize<ObservableCollection<SettingItem>>(jsonData, options)
                    ?? new ObservableCollection<SettingItem>();
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

            var fullSearch = query.Search.Trim();
            var fullSearchLower = fullSearch.ToLower();
            var matchingSettings = settingsItems.WhereSearchStartWithKeyword(fullSearchLower);
            var settingsByMatchType = matchingSettings.ClassifyByPlaceholderUsage(fullSearch);

            var processedItems = ProcessSettingItems(settingsByMatchType, fullSearch);
            var results = await CreateResultsAsync(processedItems, fullSearch, token).ConfigureAwait(false);
            AddBulkResultIfNeeded(results, processedItems, fullSearch);

            return results;
        }

        private IEnumerable<SettingItem> ProcessSettingItems(
            ILookup<SettingMatchType, SettingItem> settingsByMatchType,
            string fullSearch)
        {
            var updatedItems = settingsByMatchType[SettingMatchType.PlaceholderWithDelimiter]
                .Select(match => ProcessPlaceholderWithDelimiter(match, fullSearch));

            var cleanedUpPlaceholderItems = settingsByMatchType[SettingMatchType.PlaceholderWithoutDelimiter]
                .Select(ProcessPlaceholderWithoutDelimiter);

            return settingsByMatchType[SettingMatchType.NoPlaceholder]
                .Concat(cleanedUpPlaceholderItems)
                .Concat(updatedItems);
        }

        private static SettingItem ProcessPlaceholderWithDelimiter(SettingItem match, string fullSearch)
        {
            var clone = match.Clone();
            var remainingSearch = fullSearch.Substring(match.Keyword.Length).Trim();

            var parts = ExtractParts(remainingSearch, match);
            clone.Url = UrlUpdater.UpdateUrl(match.Url, parts)?.AbsoluteUri;

            return clone;
        }

        private static string[] ExtractParts(string remainingSearch, SettingItem match)
        {
            var placeholderCount = UrlUpdater.CountPlaceholdersUsed(match.Url);

            if (match.Delimiter == " ")
            {
                return remainingSearch.Split(new[] { ' ' }, placeholderCount, StringSplitOptions.RemoveEmptyEntries);
            }

            var delimIndex = remainingSearch.IndexOf(match.Delimiter, StringComparison.Ordinal);
            if (delimIndex < 0)
                return Array.Empty<string>();

            var afterDelimiter = remainingSearch.Substring(delimIndex + match.Delimiter.Length).Trim();
            return afterDelimiter.Split(new[] { match.Delimiter }, placeholderCount, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
        }

        private static SettingItem ProcessPlaceholderWithoutDelimiter(SettingItem item)
        {
            var clone = item.Clone();
            clone.Url = UrlUpdater.UpdateUrl(clone.Url, Array.Empty<string>())?.AbsoluteUri;
            return clone;
        }

        private async Task<List<Result>> CreateResultsAsync(IEnumerable<SettingItem> finalItems,string fullSearch,CancellationToken token)
        {
            var semaphore = new SemaphoreSlim(10, 10); 
            var tasks = finalItems.Select(async item =>
            {
                if (token.IsCancellationRequested)
                    return null;

                await semaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    return Uri.TryCreate(item.Url, UriKind.Absolute, out _)
                        ? await CreateResultAsync(item, fullSearch, token).ConfigureAwait(false)
                        : null;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.Where(r => r != null).ToList();
        }

        private void AddBulkResultIfNeeded(List<Result> results, IEnumerable<SettingItem> processedItems, string fullSearch)
        {
            var itemsToBulkOpen = processedItems.Where(x => x.AddToBulkOpenUrls).ToList();
            if (itemsToBulkOpen.Count <= 1) return;

            var bulkResult = CreateBulkOpenResult(fullSearch, itemsToBulkOpen);
            bulkResult.Score = (results.Count > 0 ? results.Max(x => x.Score) : BASE_SCORE) + BULK_SCORE_BONUS;
            results.Add(bulkResult);
        }

        public async Task<Result> CreateResultAsync(SettingItem settingItem, string fullSearch, CancellationToken token = default)
        {
            if (settingItem?.Url == null || !Uri.TryCreate(settingItem.Url, UriKind.Absolute, out var uri))
                return null;

            var iconPath = await GetIconPathAsync(settingItem, uri, token).ConfigureAwait(false);
            var score = CalculateScore(settingItem, fullSearch);

            return new Result
            {
                Title = settingItem.Title,
                SubTitle = uri.ToString(),
                Score = score,
                Action = _ => TryOpenUrl(uri),
                IcoPath = iconPath,
                ContextData = settingItem.Url
            };
        }

        private static async Task<string> GetIconPathAsync(SettingItem settingItem, Uri uri, CancellationToken token)
        {
            if (!string.IsNullOrEmpty(settingItem.IconPath))
                return settingItem.IconPath;

            var host = uri.Host;
            if (_faviconCache.TryGetValue(host, out var cachedPath))
                return cachedPath;

            var faviconUri = new Uri(string.Format(FAVICON_URL_TEMPLATE, host));
            var isAccessible = await IsFaviconAccessibleAsync(faviconUri, token).ConfigureAwait(false);
            var iconPath = isAccessible ? faviconUri.ToString() : DEFAULT_ICON_PATH;

            _faviconCache.TryAdd(host, iconPath);
            return iconPath;
        }

        private static int CalculateScore(SettingItem settingItem, string fullSearch)
        {
            var searchAfterKeyword = fullSearch.Length >= settingItem.Keyword.Length
                ? fullSearch.Substring(settingItem.Keyword.Length).Trim()
                : string.Empty;

            var titleLower = settingItem.Title.ToLower();
            var minLength = Math.Min(searchAfterKeyword.Length, titleLower.Length);

            if (minLength == 0)
                return BASE_SCORE;

            var distance = Levenshtein.GetDistance(
                searchAfterKeyword.AsSpan(0, minLength),
                titleLower.AsSpan(0, minLength)
            );

            return BASE_SCORE + (minLength - distance) * SIMILARITY_MULTIPLIER;
        }

        private bool TryOpenUrl(Uri uri)
        {
            try
            {
                Context.API.OpenUrl(uri);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> IsFaviconAccessibleAsync(Uri faviconUri, CancellationToken token = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync(faviconUri, token).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private Result CreateBulkOpenResult(string cleanSearchTerm, ICollection<SettingItem> itemsToOpen)
        {
            return new Result
            {
                Title = $"Bulk Open ({itemsToOpen.Count} items)",
                SubTitle = $"Open all matching links for '{cleanSearchTerm}'",
                Score = BULK_SCORE,
                Action = _ => TryBulkOpen(itemsToOpen),
                IcoPath = DEFAULT_ICON_PATH
            };
        }

        private bool TryBulkOpen(IEnumerable<SettingItem> itemsToOpen)
        {
            try
            {
                foreach (var item in itemsToOpen)
                {
                    if (Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
                    {
                        Context.API.OpenUrl(uri);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public Control CreateSettingPanel()
        {
            return new LinkSettings(settingsItems, Context);
        }
    }

    public static partial class UrlUpdater
    {
        [GeneratedRegex(@"\{(\d+)\}", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
        private static partial Regex PlaceholderRegex();

        [GeneratedRegex(@"\s+", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
        private static partial Regex WhitespaceRegex();

        [GeneratedRegex(@"\{\d+\}", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
        private static partial Regex PlaceholderCountRegex();

        public static int CountPlaceholdersUsed(string url)
        {
            return string.IsNullOrEmpty(url) ? 0 : PlaceholderCountRegex().Matches(url).Count;
        }

        public static Uri UpdateUrl(string url, IEnumerable<string> args)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            try
            {
                var argsList = args.ToList();
                var usedIndices = PlaceholderRegex().Matches(url)
                    .Cast<Match>()
                    .Where(m => int.TryParse(m.Groups[1].Value, out _))
                    .Select(m => int.Parse(m.Groups[1].Value))
                    .Distinct()
                    .OrderBy(i => i)
                    .ToList();

                var indexToArgMap = new Dictionary<int, string>();
                for (int i = 0; i < Math.Min(usedIndices.Count, argsList.Count); i++)
                {
                    indexToArgMap[usedIndices[i]] = argsList[i];
                }

                var updatedUrl = PlaceholderRegex().Replace(url, match =>
                {
                    if (int.TryParse(match.Groups[1].Value, out var index) &&
                        indexToArgMap.TryGetValue(index, out var replacement))
                    {
                        return Uri.EscapeDataString(replacement);
                    }
                    return string.Empty;
                });

                updatedUrl = WhitespaceRegex().Replace(updatedUrl.Trim(), " ");
                return Uri.TryCreate(updatedUrl, UriKind.Absolute, out var uri) ? uri : null;
            }
            catch
            {
                return null;
            }
        }
    }
}