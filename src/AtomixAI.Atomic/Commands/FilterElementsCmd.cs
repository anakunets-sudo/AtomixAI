using AtomixAI.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Atomic.Commands
{
    [AiInfo(
    name: "filter_elements",
    group: AtomicGroupType.Search,
    description: "Filters the incoming element list by Revit categories and/or C# classes using lightning-fast database indexing.",
    keywords: new[] { "filter", "category", "class",})]
    public class FilterElementsCmd : BaseAtomicCommand
    {
        public class SearchFilterSchema : DynamicBimContract
        {
            [AiParam(@"An array of target Revit BuiltInCategory names to keep (OR logic). 
        Examples: ['OST_Walls'], ['OST_Doors', 'OST_Windows']. 
        Leave empty if you only filter by Class.", isRequired: false)]
            public List<string> Categories { get; set; }

            [AiParam(@"The exact Revit C# class name to restrict elements (e.g., 'Wall', 'FamilyInstance', 'Floor'). 
        Use 'FamilyInstance' for all component-based families like doors, windows, furniture. 
        Leave empty if you only filter by Category.", isRequired: false)]
            public string ClassName { get; set; }
        }

        [AiParam(schema: typeof(SearchFilterSchema), type: "json", isRequired: true)]
        public override DynamicBimContract Params { get; set; }

        protected override AtomicResult Execute(ITransactionHandler handler)
        {
            UIDocument uidoc = handler.UIDoc;
            Document doc = uidoc.Document;

            System.Diagnostics.Debug.WriteLine($"[FILTER_ELEMENTS] START.");

            // 1. Извлекаем входной список ID из AtomicStorage
            if (!GetInput(out DynamicBimContract collectors).Success)
            {
                return AtomicResult.Error($"Filter failed. Input tag '{In}' was empty or invalid.");
            }

            System.Diagnostics.Debug.WriteLine($"[FILTER_ELEMENTS] collectors exist.");

            collectors.Get(BimKeys.Search.Collector, out FilteredElementCollector collector);

            if (collector == null)
            {
                return AtomicResult.Error($"Filter failed. Collector was null.");
            }

            System.Diagnostics.Debug.WriteLine($"[FILTER_ELEMENTS] collector count '{collector.GetElementCount()}'.");


            // 2. Создаем свежий коллектор Ревита строго по элементам из прошлого шага!
            // Это обходит ограничение 'collector already evaluated'
            //var collector = new FilteredElementCollector(doc, inputIds);

            using (collector)
            {
                var filterParams = (SearchFilterSchema)this.Params;

                // 3. ПРИМЕНЯЕМ ФИЛЬТР КЛАССА (Priority 1 - Выполняется первым, если передан)
                if (!string.IsNullOrWhiteSpace(filterParams?.ClassName))
                {
                    Type targetType = ResolveClassType(filterParams.ClassName);
                    if (targetType != null)
                    {
                        collector.OfClass(targetType);
                        System.Diagnostics.Debug.WriteLine($"[FILTER] Applied class filter: {targetType.Name}");
                    }
                }

                // 4. ПРИМЕНЯЕМ ФИЛЬТР КАТЕГОРИЙ (Priority 2 - Логика OR через мультикатегории)
                if (filterParams?.Categories != null && filterParams.Categories.Count > 0)
                {
                    // Исключаем невалидные категории на этапе разрешения
                    var bics = ResolveCategories(filterParams.Categories)
                        .Where(c => c != BuiltInCategory.INVALID)
                        .ToList();

                    if (bics.Count == 1)
                    {
                        collector.OfCategory(bics[0]);
                        System.Diagnostics.Debug.WriteLine($"[FILTER] Applied single category filter: {bics[0]}");
                    }
                    else if (bics.Count > 1)
                    {
                        collector.WherePasses(new ElementMulticategoryFilter(bics));
                        System.Diagnostics.Debug.WriteLine($"[FILTER] Applied multicategory filter for {bics.Count} items.");
                    }
                }

                if (collector.GetElementCount() == 0)
                {
                    return AtomicResult.Error($"Filtering resulted in 0 elements.");
                }

                // Выгружаем отфильтрованные ID элементов
                var filteredIds = collector.ToElementIds().ToList();

                System.Diagnostics.Debug.WriteLine($"[FILTER] Elements filtered to {filteredIds.Count}. Stored in '{Out}'.");

                var contract = new DynamicBimContract();

                contract.Set(BimKeys.Elements.ElementIds, filteredIds);

                // Записываем результат обратно в AtomicStorage
                return SetOutput(contract, filteredIds.Count, true, $"Filtered elements count: {filteredIds.Count}.");
            }
        }

        #region Вспомогательные методы fuzzy matching
        private static Type ResolveClassType(string className)
        {
            string target = className.Trim().ToLower();
            return typeof(Element).Assembly.GetTypes()
                .FirstOrDefault(t => t.Namespace == "Autodesk.Revit.DB" && t.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
        }

        private static List<BuiltInCategory> ResolveCategories(List<string> categoryNames)
        {
            var result = new List<BuiltInCategory>();
            var allNames = Enum.GetNames(typeof(BuiltInCategory));

            foreach (var name in categoryNames)
            {
                string target = name.Trim().Replace("\"", "").Replace("'", "");
                if (!target.StartsWith("OST_")) target = "OST_" + target;

                var match = allNames.FirstOrDefault(b => string.Equals(b, target, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    result.Add((BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), match));
                }
            }
            return result;
        }
        #endregion
    }
}
