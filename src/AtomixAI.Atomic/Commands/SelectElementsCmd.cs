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

    [AiInfo(
    name: "select_elements",
    group: AtomicGroupType.Search,
    description: "Selects elements from previous tool output. Never use as the first command. Action: Highlights elements in model.",
    keywords: new[] { "select"})]
    public class SelectElementsCmd : BaseAtomicCommand
    {
        protected override AtomicResult Execute(ITransactionHandler handler)
        {
            System.Diagnostics.Debug.WriteLine($"[SELECT_ELEMENTS] START");

            if (!GetInput(out DynamicBimContract toSelected).Success)
            {
                return AtomicResult.Error("Failed to get input data.");
            }

            toSelected.Get(BimKeys.Elements.ElementIds, out List<ElementId> elementIds);

            if (elementIds == null || elementIds.Count == 0)
            {
                return AtomicResult.Error("No element IDs found in input data.");
            }

            if (elementIds == null || elementIds.Count == 0)
                return AtomicResult.Error("Список элементов для выделения пуст.");

            // 2. Действие в Revit
            handler.UIDoc.Selection.SetElementIds(elementIds);

            // 3. УМНЫЙ ВЫХОД:
            // Передаем storageValue = null, так как мы НЕ МЕНЯЛИ данные.
            // Наш новый BaseAtomicCommand сам вызовет AtomicStorage.Link(In, Out).
            // ИИ получит в 'data' количество элементов через ExtractData(Get(In)).

            return SetOutput(null, elementIds.Count, true, $"Successfully selected {elementIds.Count} elements.");
        }
    }
}
