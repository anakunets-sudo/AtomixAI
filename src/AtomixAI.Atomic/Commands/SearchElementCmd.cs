using AtomixAI.Core;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Atomic.Commands
{

    [AtomicInfo(
    name: "search_elements",
    group: AtomicGroupType.Creation,
    description: "Complex element search using a pipeline of filters (Category, Level, Parameter, etc.",
    keywords: new[] { "wall", "line", "create" })]
    public class SearchElementCmd : IAtomicCommandSearch
    {
        public string CommandId => this.GetType().GetCustomAttribute<Core.AtomicInfoAttribute>()?.Name;

        [AtomicParam(@"CRITICAL ORDER: The array must contain at least 2 filters.
            1. The FIRST is always the Scope (e.g., {'kind': 'scope_active_view'} or {'kind': 'scope_project'}).
            2. The SECOND is the Category (e.g., {'kind': 'category', 'CategoryName': 'OST_Walls'}).
            Example: [{'kind': 'scope_active_view'}, {'kind': 'category', 'CategoryName': 'OST_Walls'}]", isRequired: true)]
        public JArray Filters { get; set; }

        public AtomicResult Execute(Dictionary<string, object> parameters)
        {
            try { 
            // 1. Получаем инструкции от ИИ
            if (!parameters.TryGetValue("filters", out var rawFilters) || !(rawFilters is JArray instructions))
            {
                System.Diagnostics.Debug.WriteLine("[SearchElementCmd]: Error - No filter instructions found in parameters.");
                return new AtomicResult { Success = false };
            }

            var factory = new AtomicSearchFactory();
            var filterChain = factory.CreateFilterChain(instructions);

            System.Diagnostics.Debug.WriteLine($"[SearchElementCmd]: Starting execution chain. Total filters: {filterChain.Count}");

            FilteredElementCollector collector = null;
            Document doc = TransactionManager.CurrentHandler.UIDoc.Document;

            foreach (var filter in filterChain)
            {
                collector = filter.Apply(doc, collector);

                // Логируем состояние после каждого фильтра
                int count = (collector != null) ? collector.GetElementCount() : 0;
                System.Diagnostics.Debug.WriteLine($"   -> Applied: {filter} (Priority: {filter.Priority}). Elements in collector: {count}");
            }

            var elementIds = collector.ToElementIds();

            System.Diagnostics.Debug.WriteLine($"[SearchElementCmd]: Finished. Final result: {elementIds.Count} elements.");

            return new AtomicResult { Success = true, Data = elementIds, Message = $"Найдено элементов: {elementIds.Count}" };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SearchElementCmd]: CRITICAL ERROR during filter chain: {ex.Message}");
                return new AtomicResult { Success = false };
            }
        }
    }
}
