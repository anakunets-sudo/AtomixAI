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
    name: "select_elements",
    group: AtomicGroupType.Search,
    description: "PHYSICAL ACTION: Highlights elements in the Revit UI. Not used as a first command. IMPORTANT: This tool does NOT search. It ONLY takes an existing tag from the previous command and makes it visible/selected for the user.",
    keywords: new[] { "select"})]
    public class SelectElementsCmd : BaseAtomicCommand
    {
        protected override AtomicResult Execute(ITransactionHandler handler)
        {

            return SetOutput(null, false);
            // 1. Пытаемся получить данные. GetInput сам проверит In или _last.
            // Если в хранилище лежит один ElementId, наш маппер в Dispatcher-е 
            // уже должен был обернуть его в список (или мы делаем это тут).
            var inputResult = GetInput(out List<ElementId> toSelected);

            Debug.WriteLine($"[{this.GetType().Name}] inputResult: {inputResult.ToString()}");

            if (!inputResult.Success)
                return inputResult; // Возвращаем ошибку "Chain broken" или "Type mismatch"

            if (toSelected == null || toSelected.Count == 0)
                return AtomicResult.Error("Список элементов для выделения пуст.");

            // 2. Действие в Revit
            handler.UIDoc.Selection.SetElementIds(toSelected);

            // 3. УМНЫЙ ВЫХОД:
            // Передаем storageValue = null, так как мы НЕ МЕНЯЛИ данные.
            // Наш новый BaseAtomicCommand сам вызовет AtomicStorage.Link(In, Out).
            // ИИ получит в 'data' количество элементов через ExtractData(Get(In)).
            return SetOutput(null, true, $"Successfully selected {toSelected.Count} elements.");
        }
    }
}
