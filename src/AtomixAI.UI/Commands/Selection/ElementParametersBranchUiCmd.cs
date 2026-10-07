using System.Collections.Generic;
using Autodesk.Revit.UI;
using AtomixAI.Core;
using AtomixAI.UI.Infrastructure;

namespace AtomixAI.UI.Commands.Selection
{
    // Элемент из выделения: развилка "Instance parameters" / "Type parameters"
    public class ElementParametersBranchUiCmd : IMenuItemUiCommand
    {
        public bool CanHandle(string itemId) =>
            SelectionMenuIds.HasPrefix(itemId, SelectionMenuIds.ElementPrefix);

        public string Execute(UIApplication app, string itemId)
        {
            string rawId = SelectionMenuIds.RawId(itemId, SelectionMenuIds.ElementPrefix);

            var items = new List<object>
            {
                new { id = SelectionMenuIds.Instance(rawId), name = MenuResponse.Label("🔹", Localizer.T("selection.instanceParams")), hasChildren = true },
                new { id = SelectionMenuIds.Type(rawId), name = MenuResponse.Label("🔸", Localizer.T("selection.typeParams")), hasChildren = true }
            };
            return MenuResponse.Items(items);
        }
    }
}
