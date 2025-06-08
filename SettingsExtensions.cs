using System.Collections.Generic;
using System.Linq;
namespace Flow.Launcher.Plugin.LinkOpener
{
    public static class SettingsExtensions
    {
        public static IEnumerable<SettingItem> WhereSearchStartWithKeyword(this IEnumerable<SettingItem> settings, string search)
        {
            foreach (var setting in settings)
            {
                if (setting.Keyword != null && search.StartsWith(setting.Keyword.Trim().ToLower()))
                {
                    yield return setting;
                }
            }
        }

        public static ILookup<SettingMatchType, SettingItem> ClassifyByPlaceholderUsage(this IEnumerable<SettingItem> settings, string search)
        {
            return settings.ToLookup(s =>
            {
                bool hasPlaceholder = UrlUpdater.CountPlaceholdersUsed(s.Url) > 0;
                bool containsDelimiter = search.Contains(s.Delimiter);

                if (!hasPlaceholder)
                    return SettingMatchType.NoPlaceholder;

                return containsDelimiter
                    ? SettingMatchType.PlaceholderWithDelimiter
                    : SettingMatchType.PlaceholderWithoutDelimiter;
            });
        }
    }
}
