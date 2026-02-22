using AtomixAI.Core;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AtomixAI.Atomic.Commands
{
    [AtomicInfo(
        name: "get_context_summary",
        group: AtomicGroupType.Knowledge,
        description: "Obtains context information at the end of command execution.",
        keywords: new[] { "How many", "which" })]
    public class GetContextSummaryCmd : IAtomicCommand
    {
        public string CommandId => "get_context_summary";

        // ✅ ИСПРАВЛЕНО: теперь это нормальное свойство, а не throw
        public string ResultAlias { get; set; }

        [AtomicParam("Alias объекта для анализа")]
        public string TargetAlias { get; set; }

        public AtomicResult Execute(Dictionary<string, object> parameters)
        {
            try
            {
                if (string.IsNullOrEmpty(TargetAlias))
                    return new AtomicResult { Success = false, Message = "TargetAlias is required." };

                var data = AtomicStorage.Get(TargetAlias);
                if (data == null)
                {
                    Debug.WriteLine($"[GetContextSummaryCmd]: Object '{TargetAlias}' not found.", nameof(GetContextSummaryCmd));
                    return new AtomicResult { Success = false, Message = $"Object '{TargetAlias}' not found in storage." };
                }

                string summary = string.Empty;

                // Если это список элементов (например, после поиска)
                if (data is List<ElementId> ids)
                {
                    summary = $"List '{TargetAlias}' contains {ids.Count} elements.";
                    Debug.WriteLine($"[GetContextSummaryCmd]: {summary}", nameof(GetContextSummaryCmd));

                    return new AtomicResult
                    {
                        Success = true,
                        Data = new { count = ids.Count, aliases = new[] { TargetAlias } },
                        Message = summary
                    };
                }

                // Если это ElementId (например, созданная стена)
                if (data is ElementId elementId)
                {
                    summary = $"Object '{TargetAlias}' is an Element (ID: {elementId.IntegerValue}).";
                    Debug.WriteLine($"[GetContextSummaryCmd]: {summary}", nameof(GetContextSummaryCmd));

                    return new AtomicResult
                    {
                        Success = true,
                        Data = new { type = "element", id = elementId.IntegerValue, alias = TargetAlias },
                        Message = summary
                    };
                }

                // Для других типов
                summary = $"Object '{TargetAlias}' is ready to use (Type: {data.GetType().Name}).";
                Debug.WriteLine($"[GetContextSummaryCmd]: {summary}", nameof(GetContextSummaryCmd));

                return new AtomicResult
                {
                    Success = true,
                    Data = new { type = data.GetType().Name, alias = TargetAlias },
                    Message = summary
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetContextSummaryCmd failed: {ex}", nameof(GetContextSummaryCmd));
                return new AtomicResult { Success = false, Message = ex.Message };
            }
        }
    }
}