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
    description: "PHYSICAL ACTION: Highlights elements in the Revit UI. " +
                  "IMPORTANT: This tool does NOT search. It ONLY takes an existing alias " +
                  "from 'search_elements' and makes it visible/selected for the user.",
    keywords: new[] { "select"})]
    public class SelectElementsCmd : BaseAtomicCommand,  IAtomicCommand
    {
        protected override AtomicResult Execute(ITransactionHandler handler)
        {
            List<ElementId> toSelected;

            var result = GetInput(out List<ElementId> inputDatas);

            if (!result.Success) 
            {
                result = GetInput(out ElementId inputData);

                if (!result.Success) return result;
                else toSelected = new List<ElementId> { inputData };
            }
            else
            {
                toSelected = inputDatas;
            }

            if (toSelected != null)
            {
                handler.UIDoc.Selection.SetElementIds(toSelected);

                return SetOutput(toSelected, true, $"Selected {toSelected.Count} elements. Stored in '{Out}'.");
            }
            else
            {
                return SetOutput(null, true);
            }
        }
    }
}
