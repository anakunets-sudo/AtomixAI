using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AtomixAI.Core;
using Autodesk.Revit.DB;

namespace AtomixAI.Atomic
{
    /// <summary>
    /// Центральный реестр типов AtomixAI. 
    /// Обеспечивает O(1) доступ к командам, фильтрам и типам Revit API.
    /// </summary>
    public static class TypeRegistry
    {
        private static readonly object _lock = new object();

        // Кэш для поисковых фильтров: [name] -> Type (ISearchFilter)
        private static Dictionary<string, Type> _filterTypes;

        // Кэш для логики (Actions/Info): [name] -> Type (IAtomicCommand)
        private static Dictionary<string, Type> _commandTypes;

        // Кэш для типов Revit API: "wall" -> typeof(Autodesk.Revit.DB.Wall)
        private static Dictionary<string, Type> _revitApiTypes;

        /// <summary>
        /// Возвращает типы фильтров поиска (ISearchFilter).
        /// </summary>
        public static Dictionary<string, Type> GetFilterTypes()
        {
            if (_filterTypes != null) return _filterTypes;
            lock (_lock)
            {
                return _filterTypes ?? (_filterTypes = ScanForTypes<ISearchFilter>());
            }
        }

        /// <summary>
        /// Возвращает типы команд (IAtomicCommandAction/Info).
        /// </summary>
        public static Dictionary<string, Type> GetCommandTypes()
        {
            if (_commandTypes != null) return _commandTypes;
            lock (_lock)
            {
                return _commandTypes ?? (_commandTypes = ScanForTypes<IAtomicCommand>());
            }
        }

        /// <summary>
        /// Быстрый поиск типа Revit API по имени (Wall, FamilyInstance и т.д.)
        /// </summary>
        public static Type GetRevitApiType(string className)
        {
            if (_revitApiTypes == null)
            {
                lock (_lock)
                {
                    if (_revitApiTypes == null) _revitApiTypes = IndexRevitApi();
                }
            }

            if (string.IsNullOrEmpty(className)) return null;
            _revitApiTypes.TryGetValue(className.ToLower(), out Type type);
            return type;
        }

        private static Dictionary<string, Type> ScanForTypes<T>()
        {
            var result = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            var targetInterface = typeof(T);

            // Сканируем текущую сборку (Atomic)
            var types = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => targetInterface.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<AtomicInfoAttribute>();
                var keys = new List<string>();

                // 1. Ключ из атрибута (priority)
                if (attr != null && !string.IsNullOrEmpty(attr.Name))
                    keys.Add(attr.Name.Replace("_", "").ToLower());

                // 2. Ключ из имени класса (fallback)
                string cleanName = type.Name
                    /*.Replace("Action", "").Replace("Command", "")
                    .Replace("Filter", "").Replace("Initializer", "")
                    .Replace("_", "")*/
                    .ToLower();

                keys.Add(cleanName);
                keys.Add(type.Name.ToLower());

                foreach (var key in keys.Distinct())
                {
                    if (string.IsNullOrEmpty(key) || result.ContainsKey(key)) continue;
                    result.Add(key, type);
                }
            }
            return result;
        }

        private static Dictionary<string, Type> IndexRevitApi()
        {
            // Индексируем только классы, наследуемые от Element
            return typeof(Element).Assembly.GetTypes()
                .Where(t => typeof(Element).IsAssignableFrom(t) && !t.IsAbstract)
                .GroupBy(t => t.Name.ToLower())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
    }
}

