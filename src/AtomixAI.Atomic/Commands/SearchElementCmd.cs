using AtomixAI.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Xml.Linq;

namespace AtomixAI.Atomic.Commands
{
    [AiInfo(
    name: "search_init",
    group: AtomicGroupType.Search,
    description: "Initializes the search boundary and gathers the initial set of elements. This command cannot be run independently, you MUST specify the search entity.",
    keywords: new[] { "search", "find", })]
    public class SearchInitCmd : BaseAtomicCommand
    {
        public class SearchInitSchema : DynamicBimContract
        {
            [AiParam(@"The exact target boundary where the search must start. Allowed values:
- 'active view':  Assign this scope to find physical elements/instances only if the user explicitly requests them 'here', 'in this view', 'on this sheet', or 'in the current view' or ALWAYS USE this if the search location is specified.
- 'project': Use this option to search for physical elements/instances when the user EXPLICITLY asks to search the entire project, such as 'everywhere', 'entire project', or 'all model elements'.
- 'selection': Use this option to find physical elements/instances when the user specifies 'selected', 'in selection', or 'currently highlighted elements'.
- 'element types': Use this option ONLY if the user explicitly requests types rather than physical instances, using words like 'types', 'symbols', or 'family definitions' (e.g., 'find door types', 'get wall types').",
            isRequired: true)]
            public string Scope { get; set; }
        }

        [AiParam(schema: typeof(SearchInitSchema), type: "json", isRequired: true)]
        public DynamicBimContract Params { get; set; }

        protected override AtomicResult Execute(ITransactionHandler handler)
        {
            UIDocument uidoc = handler.UIDoc;
            Document doc = uidoc.Document;

            var initParams = (SearchInitSchema)this.Params;

            string scope = initParams?.Scope ?? "active view";

            System.Diagnostics.Debug.WriteLine($"[SEARCH_INIT] '{scope}'.");

            FilteredElementCollector collector;

            // Переключаем логику сбора в зависимости от параметра
            switch (scope.ToLower())
            {

                case "project":
                default:
                    // Собираем вообще все элементы проекта (только экземпляры, не типы)
                    collector = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType();
                    break;

                case "selection":
                    // Забираем то, что пользователь уже выделил руками в Revit
                    collector = new FilteredElementCollector(doc, uidoc.Selection.GetElementIds());
                    break;

                case "active view":                
                    System.Diagnostics.Debug.WriteLine($"[SEARCH_INIT] activeview work.");
                    // По умолчанию собираем все видимые элементы на активном виде
                    collector = new FilteredElementCollector(doc, uidoc.ActiveView.Id);
                    break;
                case "element types":
                    collector = new FilteredElementCollector(doc)
                        .WhereElementIsElementType();
                    break;
            }

            int count = collector.GetElementCount();

            System.Diagnostics.Debug.WriteLine($"[SEARCH_INIT] collectior count'{count}'.");

            // Если область пустая (например, выбрали Selection, а ничего не выделено)
            if (collector == null || collector.GetElementCount() == 0)
            {
                return SetOutput(null, 0, false, $"Search initialization failed. No elements found in scope '{scope}'.");
            }

            System.Diagnostics.Debug.WriteLine($"[SEARCH_INIT] Scope '{scope}' initialized with {count} elements. Stored in '{Out}'.");

            var contract = new DynamicBimContract();

            contract.Set(BimKeys.Search.Collector, collector);

            //var words = Regex.Replace(scope, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ").ToLower();

            // Передаем плоский список List<ElementId> в AtomicStorage под тегом Out!
            return SetOutput(contract, count, true, $"IMPORTANT for FINAL report: initialized {scope} scope.");
        }
    }
}
