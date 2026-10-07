using Autodesk.Revit.UI;

namespace AtomixAI.UI.Infrastructure
{
    public interface IWebViewUiCommand
    {
        string Execute(UIApplication app, string itemId);
    }
}
