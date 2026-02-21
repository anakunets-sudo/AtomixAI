using AtomixAI.Main.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AtomixAI.Main
{
    [Transaction(TransactionMode.Manual)]
    public class ShowPane : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            // Принудительно обновляем ссылку на UiApp при открытии панели
            //App.UiApp = data.Application;

            // Передаем документ в Handler, чтобы он был доступен сразу
            //App.RevitProxy.ActiveDoc = data.Application.ActiveUIDocument.Document;

            var pane = data.Application.GetDockablePane(AtomixDockablePane.ID);

            pane.Show();

            return Result.Succeeded;
        }
    }
}
