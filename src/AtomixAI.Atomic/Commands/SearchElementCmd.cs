using AtomixAI.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
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
    group: AtomicGroupType.Search,
    description: "Complex element search using a pipeline of filters (Category, Level, Parameter, etc.). Use the command to search for elements when asking find, search, or how many.",
    keywords: new[] { "search", "how many", "find" })]
    public class SearchElementCmd : BaseAtomicCommand,  IAtomicCommandSearch
    {
        [AtomicParam("List of search filters. Format: [{ 'Kind': 'Category', 'CategoryName': 'OST_Walls' }, { 'Kind': 'Level', 'LevelName': 'Level 1' }]")]
        public List<Dictionary<string, object>> Filters { get; set; }

        protected override AtomicResult Execute(ITransactionHandler handler)
        {
            FilteredElementCollector collector = null;

            UIDocument uidoc = handler.UIDoc;

            /*var result = GetInput(out List<ElementId> inputData);

            if (!result.Success) return result;*/

            // 2. Логика поиска (AtomicSearchFactory)
            var factory = new AtomicSearchFactory();

            var filterChain = factory.CreateFilterChain(this.Filters);

            foreach (var filter in filterChain)
            {
                collector = filter.Apply(uidoc, collector);
            }

            var elementIds = collector?.ToElementIds().ToList() ?? new List<ElementId>();

            // 3. Управление ВЫХОДОМ (Out)
            if (elementIds.Count == 0)
            {
                return SetOutput(null, true); //false если ошибка если оборавать код, иначе он продолжится в цепочке
            }

            return SetOutput(elementIds, elementIds.Count, true, $"Found {elementIds.Count} elements. Stored in '{Out}'.");
        }
    }
}
