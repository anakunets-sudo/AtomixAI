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

    [AiInfo(
    name: "select_except",
    group: AtomicGroupType.Search,
    description: "Highlights existing elements in model, excluding the specified. Requires input from prior command. Cannot start a chain.",
    keywords: new[] { "select", "exclude" })]
    public class SelectExceptCmd : BaseAtomicCommand,  IAtomicCommand
    {
        public class SelectExceptSchema : DynamicBimContract
        {
            [AiParam("A data tag '#<tag_name>' containing ElementId to exclude. This tag is used to select the ElementId to exclude from the selection.")]
            public string ExceptTag { get; set; }
        }

        [AiParam(schema: typeof(SelectExceptSchema), type: "json")]
        public override DynamicBimContract Params { get; set; }

        protected override AtomicResult Execute(ITransactionHandler handler)
        {
            List<ElementId> toSelected;

            if(!GetInput(out DynamicBimContract inputDatas).Success || !inputDatas.Get(BimKeys.Elements.ElementIds, out List<ElementId> elementIds))
            {
                return AtomicResult.Error("No input data found. Please provide a list of ElementIds to select.");
            }

            var myParam = Params as SelectExceptSchema;

            if(myParam.ExceptTag == null)
            {
                return AtomicResult.Error("No Except parameter found. Please provide a tag name to exclude.");
            }

            if(!GetInput(out DynamicBimContract excludeDatas, myParam.ExceptTag).Success || !excludeDatas.Get(BimKeys.Elements.ElementIds, out List<ElementId> exclude))
            {
                return AtomicResult.Error($"No data found for Except tag '{myParam.ExceptTag}'. Please provide a list of ElementIds to exclude.");
            }

            toSelected = elementIds.Except(exclude).ToList();

            if (toSelected.Count == 0)
            {
                return AtomicResult.Error("No elements to select after excluding. Please check your input data and exclude tag.");
            }

            handler.UIDoc.Selection.SetElementIds(toSelected);

            return SetOutput(toSelected, toSelected.Count, true, $"Selected {toSelected.Count} elements. Stored in '{Out}'.");
        }
    }
}
