using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AtomixAI.UI.Infrastructure;

namespace AtomixAI.UI.Commands.Selection
{
    // Параметры экземпляра выбранного элемента
    public class InstanceParametersUiCmd : IMenuItemUiCommand
    {
        public bool CanHandle(string itemId) =>
            SelectionMenuIds.HasPrefix(itemId, SelectionMenuIds.InstancePrefix);

        public string Execute(UIApplication app, string itemId)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return MenuResponse.Empty();

            string rawId = SelectionMenuIds.RawId(itemId, SelectionMenuIds.InstancePrefix);
            Element el = doc.GetElement(SelectionMenuIds.ParseElementId(rawId));
            if (el == null) return MenuResponse.Empty();

            return MenuResponse.Items(ParameterMenuBuilder.Build(el, isType: false, originalElement: el));
        }
    }
}
