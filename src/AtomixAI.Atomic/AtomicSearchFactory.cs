using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using AtomixAI.Core;

namespace AtomixAI.Atomic
{
    public class AtomicSearchFactory
    {
        /// <summary>
        /// Создает цепочку фильтров на основе инструкций от ИИ.
        /// </summary>
        public List<ISearchFilter> CreateFilterChain(JArray instructions)
        {
            System.Diagnostics.Debug.WriteLine($"[AiSearchFactory]: Processing {instructions?.Count ?? 0} instructions...");

            var chain = new List<ISearchFilter>();
            if (instructions == null) return chain;

            var targetAssembly = typeof(AtomicSearchFactory).Assembly;

            // 1. Находим все типы фильтров в сборке с атрибутом [AtomicInfo]
            // В идеале это должно быть закэшировано в TypeRegistry
            // Ищем типы только внутри неё (это мгновенно и безопасно)
            var filterTypes = targetAssembly.GetTypes()
                .Where(p => typeof(ISearchFilter).IsAssignableFrom(p) && !p.IsInterface)
                .Select(t => new { Type = t, Info = t.GetCustomAttribute<AtomicInfoAttribute>() })
                .Where(x => x.Info != null)
                .ToDictionary(x => x.Info.Name.Replace("_", "").ToLower(), x => x.Type);

            foreach (var token in instructions)
            {
                if (!(token is JObject filterObj)) continue;

                string rawKind = (filterObj["kind"] ?? filterObj["Kind"])?.ToString();
                if (string.IsNullOrEmpty(rawKind)) continue;

                string cleanKind = rawKind.Replace("_", "").ToLower();

                if (filterTypes.TryGetValue(cleanKind, out Type filterType))
                {
                    var filter = (ISearchFilter)Activator.CreateInstance(filterType);

                    // 2. Наполняем свойства фильтра данными
                    MapAtomicParams(filter, filterObj);

                    chain.Add(filter);
                }
            }

            // 3. Сортируем по Priority (Initializer(0) -> Fast(2) -> Slow(10))
            return chain.OrderBy(f => f.Priority).ToList();
        }

        private static void MapAtomicParams(ISearchFilter filter, JObject data)
        {
            var props = filter.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                var attr = prop.GetCustomAttribute<AtomicParamAttribute>();
                if (attr == null) continue;

                // Ищем значение в JSON по имени свойства или значению из атрибута
                JToken val = data.GetValue(prop.Name, StringComparison.OrdinalIgnoreCase)
                          ?? data.GetValue("value", StringComparison.OrdinalIgnoreCase);

                if (val == null || val.Type == JTokenType.Null) continue;

                try
                {
                    object convertedValue = ConvertValue(val, prop.PropertyType);
                    prop.SetValue(filter, convertedValue);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mapping Error]: {filter.GetType().Name}.{prop.Name} -> {ex.Message}");
                }
            }
        }

        private static object ConvertValue(JToken token, Type targetType)
        {
            if (targetType == typeof(string)) return token.ToString();
            if (targetType == typeof(double)) return token.Value<double>(); // Здесь можно добавить ParseToRevitFeet
            if (targetType == typeof(bool)) return token.Value<bool>();
            if (targetType.IsEnum) return Enum.Parse(targetType, token.ToString(), true);

            return token.ToObject(targetType);
        }
    }
}
