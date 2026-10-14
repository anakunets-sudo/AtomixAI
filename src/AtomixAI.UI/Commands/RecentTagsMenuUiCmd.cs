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
                        name = MenuResponse.Label("", Localizer.T("menu.modelData")),
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
                        var data = entry["data"] as Newtonsoft.Json.Linq.JObject;
                        string tooltip = BuildTooltip(data);
                        string encodedId = WebUtility.HtmlEncode(chipId);
                        string encodedTooltip = WebUtility.HtmlEncode(tooltip);
                        string encodedLabel = WebUtility.HtmlEncode(chipLabel);

                        return (object)new
                        {
                            id = chipId,
                            chipId,
                            chipLabel,
                            chipTooltip = tooltip,
                            isModelChip = true,
                            name = $"<span class=\"bim-chip\" data-chip-id=\"{encodedId}\" data-tooltip=\"{encodedTooltip}\">{encodedLabel}</span>",
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

        private static string BuildTooltip(Newtonsoft.Json.Linq.JObject? data)
        {
            if (data == null) return string.Empty;

            var parts = new System.Collections.Generic.List<string>();
            string? parameterName = GetValue(data, "param_Name");
            string? parameterValue = GetValue(data, "param_Value");
            string? unitLabel = GetValue(data, "param_Unit_Label");

            if (parameterName != null && parameterValue != null)
                parts.Add($"Parameter: {parameterName} = {parameterValue}" + (string.IsNullOrEmpty(unitLabel) ? string.Empty : $" {unitLabel}"));
            AddPart(parts, "Element", GetValue(data, "element_Name"));
            AddPart(parts, "Category", GetValue(data, "element_Сategory"));
            AddPart(parts, "Id", GetValue(data, "element_Id"));
            return string.Join("\n", parts);
        }

        private static string? GetValue(Newtonsoft.Json.Linq.JObject data, string key)
        {
            var token = data[key];
            return token == null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null ? null : token.ToString();
        }

        private static void AddPart(System.Collections.Generic.List<string> parts, string label, string? value)
        {
            if (!string.IsNullOrEmpty(value)) parts.Add($"{label}: {value}");
        }
    }
}
