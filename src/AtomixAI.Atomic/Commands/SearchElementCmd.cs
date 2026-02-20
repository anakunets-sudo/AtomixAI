using AtomixAI.Bridge;
using AtomixAI.Core;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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

        [AtomicParam("Список фильтров. Пример: [{'Kind':'category', 'Value':'Walls'}]", isRequired: true)]
        public List<FilterInstruction> Filters { get; set; } // КЛЮЧЕВОЕ ИЗМЕНЕНИЕ

        public AtomicResult Execute(Dictionary<string, object> parameters)
        {
            System.Diagnostics.Debug.WriteLine("[SearchElementCmd]: Метод Execute вызван.");

            if (Filters != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SearchElementCmd]: Получены фильтры из JSON: {Filters.ToString()}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[SearchElementCmd]: ОШИБКА: Параметр Filters пуст или не десериализован!");
            }
            parameters.TryGetValue("Filters", out var rawData);

            var factory = new SearchFactory();
            var filterChain = factory.CreateLogic(rawData);

            var collector = new FilteredElementCollector(TransactionManager.CurrentHandler.UIDoc.Document);

            if (!filterChain.Any(f => f is ISearchInitializer))
            {
                filterChain.Insert(0, new ActiveViewFilterInitializer());
            }

            // Применяем фильтры по очереди (сначала быстрые, потом медленные)
            foreach (ISearchFilter filter in filterChain)
            {
                collector = filter.Apply(TransactionManager.CurrentHandler.UIDoc.Document, collector);
            }

            var results = collector.ToElementIds();

            System.Diagnostics.Debug.WriteLine($"Serched elements: {results.Count}.", "SearchElementCmd");

            return new AtomicResult { Success = true, Data = results, Message = $"Searched elements: {results.Count}." };
        }
    }
}
