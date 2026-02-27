using AtomixAI.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AtomixAI.Atomic
{
    public class AtomicSearchFactory
    {
        private static Dictionary<string, Type> _filterCache;

        /// <summary>
        /// Creates a filter chain based on instructions from AI (List of Dictionaries).
        /// </summary>
        public List<ISearchFilter> CreateFilterChain(List<Dictionary<string, object>> instructions)
        {
            System.Diagnostics.Debug.WriteLine($"[AiSearchFactory]: Processing {instructions?.Count ?? 0} instructions...");

            var chain = new List<ISearchFilter>();
            if (instructions == null || instructions.Count == 0) return chain;

            // 1. Caching filter types for performance
            if (_filterCache == null)
            {
                _filterCache = Assembly.GetAssembly(typeof(ISearchFilter))
                    .GetTypes()
                    .Where(p => typeof(ISearchFilter).IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract)
                    .Select(t => new { Type = t, Info = t.GetCustomAttribute<AtomicInfoAttribute>() })
                    .Where(x => x.Info != null)
                    .ToDictionary(x => x.Info.Name.Replace("_", "").ToLower(), x => x.Type);
            }

            foreach (var dict in instructions)
            {
                // Find the filter type by the "Kind" or "kind" key
                if (!dict.TryGetValue("Kind", out var rawKind) && !dict.TryGetValue("kind", out rawKind))
                    continue;

                string cleanKind = rawKind?.ToString().Replace("_", "").ToLower();

                if (!string.IsNullOrEmpty(cleanKind) && _filterCache.TryGetValue(cleanKind, out Type filterType))
                {
                    var filter = (ISearchFilter)Activator.CreateInstance(filterType);

                    // 2. Populate the filter properties with data from the Dictionary
                    MapAtomicParams(filter, dict);

                    chain.Add(filter);
                }
            }

            // 3. Sort by Priority (Initializer(0) -> Category(2) -> Slow(10))
            return chain.OrderBy(f => f.Priority).ToList();
        }

        private static void MapAtomicParams(ISearchFilter filter, Dictionary<string, object> data)
        {
            var props = filter.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                var attr = prop.GetCustomAttribute<AtomicParamAttribute>();
                if (attr == null) continue;

                // Find the value in the dictionary by property name or "Value"/"value" key
                object rawValue = null;
                if (!data.TryGetValue(prop.Name, out rawValue))
                {
                    if (!data.TryGetValue("Value", out rawValue))
                        data.TryGetValue("value", out rawValue);
                }

                if (rawValue == null) continue;

                try
                {
                    object convertedValue = ConvertValue(rawValue, prop.PropertyType);
                    prop.SetValue(filter, convertedValue);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mapping Error]: {filter.GetType().Name}.{prop.Name} -> {ex.Message}");
                }
            }
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;

            // Если целевой тип - число (double), используем твой Util
            if (targetType == typeof(double))
            {
                // Передаем весь объект (строку "3000mm" или число 3000) в твой Util
                return Util.ParseToRevitFeet(value);
            }

            if (targetType == typeof(string)) return value.ToString();
            if (targetType == typeof(bool)) return Convert.ToBoolean(value);

            if (targetType.IsEnum)
                return Enum.Parse(targetType, value.ToString(), true);

            // Для JToken (если прилетел из Newtonsoft)
            if (value is JToken token)
                return token.ToObject(targetType);

            return value;
        }
    }
}
