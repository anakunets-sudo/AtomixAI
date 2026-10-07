using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AtomixAI.Core;

namespace AtomixAI.UI.Commands.Selection
{
    // Список текущего выделения. Используется пунктом "Selection" и после выбора на виде.
    public static class SelectionMenuBuilder
    {
        public static List<object> Build(UIDocument uiDoc)
        {
            Document doc = uiDoc.Document;
            var items = new List<object>();

            var selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds.Count == 0)
            {
                items.Add(new
                {
                    id = SelectionMenuIds.PickInView,
                    name = MenuResponse.Label("🎯", Localizer.T("selection.pickInView")),
                    hasChildren = true
                });
                return items;
            }

            foreach (var id in selectedIds)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;

                string categoryName = el.Category?.Name ?? Localizer.T("selection.noCategory");
                items.Add(new
                {
                    id = SelectionMenuIds.Element(id),
                    name = MenuResponse.Label("📦", $"{categoryName} [ID: {id.GetIdValue()}]"),
                    hasChildren = true
                });
            }
            return items;
        }
    }
}
