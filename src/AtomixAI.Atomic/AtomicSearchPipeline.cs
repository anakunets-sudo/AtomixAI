using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace AtomixAI.Atomic
{
    public class AtomicSearchPipeline
    {
        // Словарь Инициализаторов (Priority 0) - создают "ведро" коллектора
        private static readonly Dictionary<string, Func<UIDocument, FilteredElementCollector>> _initializers
            = new Dictionary<string, Func<UIDocument, FilteredElementCollector>>(StringComparer.OrdinalIgnoreCase)
            {
            { "scope_active_view", uidoc => new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id) },
            { "scope_project", uidoc => new FilteredElementCollector(uidoc.Document) }
            };

        // Словарь Быстрых фильтров (Priority 2-4) - сужают выборку на уровне индексов Revit
        private static readonly Dictionary<string, Func<FilteredElementCollector, Dictionary<string, object>, FilteredElementCollector>> _quickFilters
            = new Dictionary<string, Func<FilteredElementCollector, Dictionary<string, object>, FilteredElementCollector>>(StringComparer.OrdinalIgnoreCase)
            {
            // Фильтр по категории (поддерживает как строку, так и массив строк для OR-логики!)
            { "category", (collector, data) =>
                {
                    if (!data.TryGetValue("CategoryName", out var rawCategory) || rawCategory == null) return collector;

                    var bics = ResolveCategories(rawCategory);
                    if (bics.Count == 0) return collector;
                    if (bics.Count == 1) return collector.OfCategory(bics[0]);

                    // Если категорий несколько — задействуем ElementMulticategoryFilter
                    return collector.WherePasses(new ElementMulticategoryFilter(bics));
                }
            },
            // Фильтр по уровням (поддерживает массив ID уровней для OR-логики!)
            { "filter_by_levels", (collector, data) =>
                {
                    if (!data.TryGetValue("LevelIds", out var rawLevelIds) || rawLevelIds == null) return collector;

                    var levelIds = ResolveElementIds(rawLevelIds);
                    if (levelIds.Count == 0) return collector;

                    var levelFilters = levelIds.Select(id => new ElementLevelFilter(id)).Cast<ElementFilter>().ToList();
                    if (levelFilters.Count == 1) return collector.WherePasses(levelFilters[0]);

                    return collector.WherePasses(new LogicalOrFilter(levelFilters));
                }
            }
            };

        public List<ElementId> Run(UIDocument uidoc, List<Dictionary<string, object>> instructions)
        {
            if (instructions == null || instructions.Count == 0) return new List<ElementId>();

            FilteredElementCollector collector = null;

            // ШАГ 1: Поиск и запуск Инициализатора (Ищем первую инструкцию, которая есть в _initializers)
            foreach (var inst in instructions)
            {
                if (inst.TryGetValue("Kind", out var kindObj) && _initializers.TryGetValue(kindObj.ToString(), out var initializer))
                {
                    collector = initializer(uidoc);
                    Debug.WriteLine($"[PIPELINE] Initialized collector via: '{kindObj}'");
                    break;
                }
            }

            // Подушка безопасности: если ИИ забыл прислать область видимости, создаем дефолт по активному виду
            if (collector == null)
            {
                collector = _initializers["scope_active_view"](uidoc);
                Debug.WriteLine("[PIPELINE] WARNING: No initializer found. Defaulted to active view.");
            }

            // Хранилище для отложенных медленных фильтров (параметров)
            var slowFiltersInstructions = new List<Dictionary<string, object>>();

            // ШАГ 2: Запуск Быстрых фильтров в один проход
            foreach (var inst in instructions)
            {
                if (!inst.TryGetValue("Kind", out var kindObj) || kindObj == null) continue;
                string kind = kindObj.ToString();

                // Если это медленный фильтр по параметрам — откладываем его на финал
                if (kind.Equals("parameter", StringComparison.OrdinalIgnoreCase))
                {
                    slowFiltersInstructions.Add(inst);
                    continue;
                }

                // Если это быстрый фильтр — мгновенно применяем его к нашему коллектору
                if (_quickFilters.TryGetValue(kind, out var quickFilter))
                {
                    collector = quickFilter(collector, inst);
                    Debug.WriteLine($"[PIPELINE] Applied quick filter: '{kind}'");
                }
            }

            // ШАГ 3: Запуск Тяжелого процессора параметров (Priority 10)
            if (slowFiltersInstructions.Count > 0 && collector != null)
            {
                Debug.WriteLine($"[PIPELINE] Passing narrowed collector to AtomicParameterFilter. Instructions: {slowFiltersInstructions.Count}");

                // Внедряем наш тяжелый фильтр! Переменная collector успешно перезаписывается отфильтрованным результатом
                collector = AtomicParameterFilter.Execute(collector, slowFiltersInstructions, uidoc.Document);
            }

            // Если после всех фильтраций коллектор не null, лениво выгружаем строго отфильтрованные ID для AtomicStorage
            if (collector != null)
            {
                return collector.ToElementIds().ToList();
            }

            return new List<ElementId>();
        }

        #region Вспомогательные методы разбора типов (Fuzzy Matching)
        private static List<BuiltInCategory> ResolveCategories(object rawCategory)
        {
            var result = new List<BuiltInCategory>();
            var names = new List<string>();

            if (rawCategory is JArray jArray) names = jArray.ToObject<List<string>>();
            else names.Add(rawCategory.ToString());

            var allNames = Enum.GetNames(typeof(BuiltInCategory));
            foreach (var name in names)
            {
                string target = name.Trim().Replace("\"", "").Replace("'", "");
                var variants = new List<string> { target };
                if (!target.StartsWith("OST_")) variants.Add("OST_" + target);

                foreach (var variant in variants)
                {
                    var match = allNames.FirstOrDefault(b => string.Equals(b, variant, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        result.Add((BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), match));
                        break;
                    }
                }
            }
            return result;
        }

        private static List<ElementId> ResolveElementIds(object rawIds)
        {
            var result = new List<ElementId>();
            var ids = new List<string>();

            if (rawIds is JArray jArray) ids = jArray.ToObject<List<string>>();
            else ids.Add(rawIds.ToString());

            foreach (var idStr in ids)
            {
                if (int.TryParse(idStr, out int idInt))
                {
#if REVIT2024_OR_GREATER
                result.Add(new ElementId((long)idInt));
#else
                    result.Add(new ElementId(idInt));
#endif
                }
            }
            return result;
        }
        #endregion
    }
}
