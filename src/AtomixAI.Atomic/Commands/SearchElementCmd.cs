using AtomixAI.Core;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace AtomixAI.Atomic.Commands
{
    [AtomicInfo(
        name: "search_elements",
        group: AtomicGroupType.Creation,
        description: "Complex element search using a pipeline of filters (Category, Level, Parameter, etc.",
        keywords: new[] { "wall", "line", "create" })]
    public class SearchElementCmd : IAtomicCommandSearch
    {
        public string CommandId => this.GetType().GetCustomAttribute<AtomicInfoAttribute>()?.Name;
        [AtomicParam("A unique identifier to save the command's output into global memory for future steps (e.g., 'CreatedWall_1').")]
        public string ResultAlias { get; set; }

        [AtomicParam("Список фильтров. Пример: [{'Kind':'category', 'Value':'Walls'}]", isRequired: true)]
        public List<FilterInstruction> Filters { get; set; }

        public AtomicResult Execute(Dictionary<string, object> parameters)
        {
            try
            {
                if (parameters == null)
                    return new AtomicResult { Success = false, Message = "Parameters dictionary is null." };

                parameters.TryGetValue("filters", out var rawData);

                JArray instructions = null;

                // Accept raw JSON string as well as JArray or List<FilterInstruction>
                if (rawData is string rawStr)
                {
                    try
                    {
                        var parsed = JToken.Parse(rawStr);
                        instructions = parsed is JArray ja ? ja : new JArray(parsed);
                    }
                    catch (Exception parseEx)
                    {
                        Debug.WriteLine($"SearchElementCmd: failed to parse filters JSON: {parseEx}", nameof(SearchElementCmd));
                        return new AtomicResult { Success = false, Message = "Failed to parse filters JSON." };
                    }
                }

                // ✅ ИСПРАВЛЕНО: используем AtomicSearchFactory вместо SearchFactory
                var factory = new AtomicSearchFactory();
                var filterChain = factory.CreateFilterChain(instructions) ?? new List<ISearchFilter>();

                // Validate Revit context
                var handler = TransactionManager.CurrentHandler;
                if (handler == null || handler.UIDoc == null || handler.UIDoc.Document == null)
                {
                    Debug.WriteLine("SearchElementCmd: Revit document/context is not available.", nameof(SearchElementCmd));
                    return new AtomicResult { Success = false, Message = "No active Revit document (TransactionManager.CurrentHandler or UIDoc is null)." };
                }

                var doc = handler.UIDoc.Document;
                var collector = new FilteredElementCollector(doc);

                // Ensure initializer exists
                if (!filterChain.Any(f => f is ISearchInitializer))
                {
                    filterChain.Insert(0, new ActiveViewFilterInitializer());
                }

                // Apply filters with safety checks
                foreach (var filter in filterChain)
                {
                    if (filter == null)
                    {
                        Debug.WriteLine("SearchElementCmd: encountered null filter in chain.", nameof(SearchElementCmd));
                        return new AtomicResult { Success = false, Message = "Encountered null filter in chain." };
                    }

                    var next = filter.Apply(doc, collector);
                    if (next == null)
                    {
                        Debug.WriteLine($"SearchElementCmd: filter {filter.GetType().Name} returned null collector.", nameof(SearchElementCmd));
                        return new AtomicResult { Success = false, Message = $"Filter {filter.GetType().Name} returned null collector." };
                    }

                    collector = next;
                }

                var results = collector?.ToElementIds() ?? Enumerable.Empty<ElementId>();
                var resultsList = results.ToList();

                Debug.WriteLine($"SearchElementCmd: Searched elements: {resultsList.Count}.", nameof(SearchElementCmd));

                // ✅ ПРОСТО ВОЗВРАЩАЕМ РЕЗУЛЬТАТ
                // ToolDispatcher сохранит в AtomicStorage автоматически, если ResultAlias задан!
                return new AtomicResult
                {
                    Success = true,
                    Data = resultsList,
                    Message = $"Searched elements: {resultsList.Count}."
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SearchElementCmd failed: {ex}", nameof(SearchElementCmd));
                return new AtomicResult { Success = false, Message = ex.Message, Data = null };
            }
        }
    }
}