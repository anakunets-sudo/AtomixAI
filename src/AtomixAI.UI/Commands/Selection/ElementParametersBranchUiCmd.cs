using System.Collections.Generic;
using System.Net;
using Autodesk.Revit.DB;
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
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return MenuResponse.Empty();

            string rawId = SelectionMenuIds.RawId(itemId, SelectionMenuIds.ElementPrefix);
            Element? element = doc.GetElement(SelectionMenuIds.ParseElementId(rawId));
            if (element == null) return MenuResponse.Empty();

            var items = new List<object>();            

            items.Add(new
            {
                id = SelectionMenuIds.Instance(rawId),
                name = WebUtility.HtmlEncode(Localizer.T("selection.instanceParams")),
                hasChildren = true
            });
            items.Add(new
            {
                id = SelectionMenuIds.Type(rawId),
                name = WebUtility.HtmlEncode(Localizer.T("selection.typeParams")),
                hasChildren = true
            });
            object? name = ParameterMenuBuilder.BuildNameItem(element);
            if (name != null) items.Add(name);

            object? id = ParameterMenuBuilder.BuildIdItem(element);
            if (id != null) items.Add(id);

            object? categoryParameter = ParameterMenuBuilder.BuildBuiltInParameterItem(
                element, BuiltInParameter.ELEM_CATEGORY_PARAM, isType: false);
            if (categoryParameter != null) items.Add(categoryParameter);

            object? typeNameParameter = ParameterMenuBuilder.BuildBuiltInParameterItem(
                element, BuiltInParameter.SYMBOL_NAME_PARAM, isType: true);
            if (typeNameParameter != null) items.Add(typeNameParameter);

            object? familyNameParameter = ParameterMenuBuilder.BuildBuiltInParameterItem(
                element, BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM, isType: true);
            if (familyNameParameter != null) items.Add(familyNameParameter);

            return MenuResponse.Items(items);
        }
    }
}
