using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using AtomixAI.Core;
using AtomixAI.UI.Infrastructure;

namespace AtomixAI.UI.Commands.Selection
{
    // "Pick elements in view": интерактивный выбор на активном виде, затем список выделенного
    public class PickElementsInViewUiCmd : IMenuItemUiCommand
    {
        public bool CanHandle(string itemId) =>
            string.Equals(itemId, SelectionMenuIds.PickInView, StringComparison.OrdinalIgnoreCase);

        public string Execute(UIApplication app, string itemId)
        {
            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null) return MenuResponse.Empty();

            try
            {
                IList<Reference> picked = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    Localizer.T("selection.pickPrompt"));

                var ids = picked
                    .Select(r => r.ElementId)
                    .Where(id => id != ElementId.InvalidElementId)
                    .Distinct()
                    .ToList();

                uiDoc.Selection.SetElementIds(ids);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PickElementsInView] {ex.Message}");
            }

            return MenuResponse.Items(SelectionMenuBuilder.Build(uiDoc));
        }
    }
}
