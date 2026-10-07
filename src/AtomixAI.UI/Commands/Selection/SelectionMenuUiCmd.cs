using System;
using Autodesk.Revit.UI;
using AtomixAI.UI.Infrastructure;

namespace AtomixAI.UI.Commands.Selection
{
    // "Selection": выделенные элементы или пункт "Pick elements in view"
    public class SelectionMenuUiCmd : IMenuItemUiCommand
    {
        public bool CanHandle(string itemId) =>
            string.Equals(itemId, SelectionMenuIds.Root, StringComparison.OrdinalIgnoreCase);

        public string Execute(UIApplication app, string itemId)
        {
            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null) return MenuResponse.Empty();

            return MenuResponse.Items(SelectionMenuBuilder.Build(uiDoc));
        }
    }
}
