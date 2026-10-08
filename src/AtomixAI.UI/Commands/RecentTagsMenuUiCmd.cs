using System;
using System.Linq;
using System.Net;
using Autodesk.Revit.UI;
using AtomixAI.Core;
using AtomixAI.UI.Infrastructure;

namespace AtomixAI.UI.Commands
{
    public class RecentTagsMenuUiCmd : IMenuItemUiCommand
    {
        public bool CanHandle(string itemId) =>
            string.Equals(itemId, "recent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(itemId, "tags", StringComparison.OrdinalIgnoreCase);

        public string Execute(UIApplication app, string itemId)
        {
            if (string.Equals(itemId, "recent", StringComparison.OrdinalIgnoreCase))
            {
                return MenuResponse.Items(new object[]
                {
                    new
                    {
                        id = "tags",
                        name = MenuResponse.Label("🏷️", "Tags"),
                        hasChildren = true
                    }
                });
            }

            var items = AtomicStorage.GetCurrentContext()
                .Select(tag => (object)new
                {
                    id = tag,
                    name = WebUtility.HtmlEncode(tag),
                    hasChildren = false,
                    meta = new
                    {
                        paramName = tag,
                        paramType = "Storage Tag"
                    }
                })
                .ToList();

            return MenuResponse.Items(items);
        }
    }
}
