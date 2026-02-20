using Autodesk.Revit.UI;
//using AtomixAI.Main.Views;
using AtomixAI.Main.ViewModels;

namespace AtomixAI.Main.Infrastructure
{
    public static class PanelUtils
    {
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("73A16A0B-4E31-4F9E-9E8D-68B6615C9C9A"));

        public static void Register(UIControlledApplication app)
        {
            //var vm = new AtomixPanelViewModel();
            //var view = new AtomixPanelPage { DataContext = vm };
            //app.RegisterDockablePane(PaneId, "AtomixAI Console", view as IDockablePaneProvider);
        }
    }
}
