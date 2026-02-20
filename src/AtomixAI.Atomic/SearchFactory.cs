using AtomixAI.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AtomixAI.Bridge
{
    public class SearchFactory
    {
        // Кэшируем типы фильтров один раз, чтобы не сканировать сборки при каждом поиске
        private static readonly List<Type> _filterTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => typeof(ISearchFilter).IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract)
            .ToList();

        /// <summary>
        /// Принимает object, чтобы гибко обрабатывать JArray или List, пришедшие из Bridge.
        /// </summary>
        public List<ISearchFilter> CreateLogic(object inputData)
        {
            var chain = new List<ISearchFilter>();
            if (inputData == null) return chain;

            // 1. Универсальное приведение входящих данных к списку инструкций
            List<FilterInstruction> instructions;
            if (inputData is List<FilterInstruction> list)
                instructions = list;
            else if (inputData is JArray jArray)
                instructions = jArray.ToObject<List<FilterInstruction>>();
            else
                return chain;

            foreach (var ins in instructions)
            {
                if (string.IsNullOrEmpty(ins.Kind)) continue;

                // 2. Поиск фильтра по имени в AtomicInfo (игнорируя регистр и подчеркивания)
                string targetKind = ins.Kind.Replace("_", "").ToLower();
                var type = _filterTypes.FirstOrDefault(t =>
                    t.GetCustomAttribute<AtomicInfoAttribute>()?.Name.Replace("_", "").ToLower() == targetKind);

                if (type != null)
                {
                    var filter = (ISearchFilter)Activator.CreateInstance(type);

                    // 3. Мапим значение Value в нужное свойство
                    MapValueToFilter(filter, ins.Value);
                    chain.Add(filter);
                }
            }

            // 4. Сортировка по Priority (0 - Scope, 2 - Category, 10 - Parameters)
            return chain.OrderBy(c => c.Priority).ToList();
        }

        private void MapValueToFilter(ISearchFilter filter, string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            // Ищем свойство, которое помечено как основное (например, CategoryName)
            var props = filter.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                var attr = prop.GetCustomAttribute<AtomicParamAttribute>();
                if (attr == null) continue;

                // Если свойство строковое, записываем туда Value
                if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(filter, value);
                    break; // Записываем в первое подходящее и выходим
                }
            }
        }
    }
}