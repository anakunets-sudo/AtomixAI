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
            string.Equals(itemId, "tags", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(itemId, "model-data", StringComparison.OrdinalIgnoreCase);

        public string Execute(UIApplication app, string itemId)
        {
            if (string.Equals(itemId, "recent", StringComparison.OrdinalIgnoreCase))
            {
                return MenuResponse.Items(new object[]
                {
                    new
                    {
                        id = "tags",
                        name = MenuResponse.Label("", Localizer.T("menu.tags")),
                        hasChildren = true
                    },
                    new
                    {
                        id = "model-data",
                        name = MenuResponse.Label("", Localizer.T("menu.data")),
                        hasChildren = true
                    }
                });
            }

            if (string.Equals(itemId, "model-data", StringComparison.OrdinalIgnoreCase))
            {
                var chips = ChipStorage.Shared.GetApprovedEntries()
                    .Select(entry =>
                    {
                        string chipId = entry["id"]?.ToString() ?? string.Empty;
                        string chipLabel = entry["label"]?.ToString() ?? chipId;
                        var data = entry["data"] as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
                        string encodedLabel = WebUtility.HtmlEncode(chipLabel);

                        return (object)new
                        {
                            id = chipId,
                            chipPayload = new ChipPayload(chipId, chipLabel, data),
                            name = encodedLabel,
                            hasChildren = false
                        };
                    })
                    .ToList();

                return MenuResponse.Items(chips);
            }

            var items = AtomicStorage.GetUserTags()
                .Select(tag =>
                {
                    string alias = tag.StartsWith("#", StringComparison.Ordinal) ? tag : $"#{tag}";
                    return (object)new
                    {
                        id = tag,
                        name = WebUtility.HtmlEncode(alias),
                        hasChildren = false,
                        insertAlias = alias
                    };
                })
                .ToList();

            return MenuResponse.Items(items);
        }
    }
}
