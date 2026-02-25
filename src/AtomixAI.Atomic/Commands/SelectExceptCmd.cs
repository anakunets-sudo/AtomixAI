using AtomixAI.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
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
    name: "select_except",
    group: AtomicGroupType.Search,
    description: "PHYSICAL ACTION: Highlights elements in the Revit UI excluding elements by the Exclude alias. " +
                  "IMPORTANT: This tool does NOT search. It ONLY takes an existing alias " +
                  "from 'search_elements' and makes it visible/selected for the user.",
    keywords: new[] { "select", "exclude" })]
    public class SelectExceptCmd : BaseAtomicCommand,  IAtomicCommand
    {
        [AtomicParam("OUTPUT_PORT: Creates a new data alias containing ElementId to exclude. This alias is used to select the ElementId to exclude from the selection.")]
        public string Exclude { get; set; }

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
                var exclude =  AtomicStorage.Get(Exclude);

                if (exclude is ElementId id)
                {
                    toSelected = toSelected.Except(new ElementId[] { id }).ToList();
                }
                else if (exclude is List<ElementId> ids)
                {
                    toSelected = toSelected.Except(ids).ToList();
                }

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
