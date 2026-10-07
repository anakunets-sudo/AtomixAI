using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AtomixAI.Core
{
    // Строки интерфейса из wwwroot/i18n/{lang}.json. Общий источник для C# и WebView (JS).
    public static class Localizer
    {
        public const string FallbackLanguage = "en";

        private static JObject _strings = new JObject();

        public static string Language { get; private set; } = FallbackLanguage;

        // Вызывать в главном потоке Revit: CurrentUICulture фоновых потоков не совпадает с языком Revit
        public static void Initialize(string i18nDir, CultureInfo culture)
        {
            Language = FallbackLanguage;
            JObject merged = Load(i18nDir, FallbackLanguage) ?? new JObject();

            string requested = culture.TwoLetterISOLanguageName;
            if (!string.Equals(requested, FallbackLanguage, StringComparison.OrdinalIgnoreCase))
            {
                JObject? localized = Load(i18nDir, requested);
                if (localized != null)
                {
                    merged.Merge(localized, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                    Language = requested.ToLowerInvariant();
                }
            }

            _strings = merged;
        }

        public static string T(string key)
        {
            return _strings[key] is JValue value && value.Type == JTokenType.String
                ? (string)value!
                : key;
        }

        // Revit может первым загрузить старую Newtonsoft.Json из другой надстройки.
        // Перегрузка JToken.ToString(Formatting) в ней отсутствует, метод без параметров совместим.
        public static string ToJson() => _strings.ToString();

        private static JObject? Load(string dir, string lang)
        {
            string path = Path.Combine(dir, lang.ToLowerInvariant() + ".json");
            if (!File.Exists(path)) return null;

            try
            {
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Localizer] {path}: {ex.Message}");
                return null;
            }
        }
    }
}
