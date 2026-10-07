using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AtomixAI.Core;
using AtomixAI.UI.Infrastructure;

namespace AtomixAI.UI.Commands.Selection
{
    // Параметры типа выбранного элемента
    public class TypeParametersUiCmd : IMenuItemUiCommand
    {
        public bool CanHandle(string itemId) =>
            SelectionMenuIds.HasPrefix(itemId, SelectionMenuIds.TypePrefix);

        public string Execute(UIApplication app, string itemId)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return MenuResponse.Empty();

            string rawId = SelectionMenuIds.RawId(itemId, SelectionMenuIds.TypePrefix);
            Element instanceEl = doc.GetElement(SelectionMenuIds.ParseElementId(rawId));
            if (instanceEl == null) return MenuResponse.Empty();

            ElementId typeId = instanceEl.GetTypeId();
            Element? typeEl = typeId == ElementId.InvalidElementId ? null : doc.GetElement(typeId);
            if (typeEl == null)
            {
                return MenuResponse.Items(new List<object>
                {
                    new { id = "none", name = MenuResponse.Label("❌", Localizer.T("selection.noType")), hasChildren = false }
                });
            }

            return MenuResponse.Items(ParameterMenuBuilder.Build(typeEl, isType: true, originalElement: instanceEl));
        }
    }
}
