using System;
using System.Collections.Generic;
using System.Net;
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

            var groups = new List<KeyValuePair<string, List<Element>>>();
            var byCategory = new Dictionary<string, List<Element>>(StringComparer.CurrentCultureIgnoreCase);

            foreach (var id in selectedIds)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;

                string categoryName = el.Category?.Name ?? Localizer.T("selection.noCategory");
                if (!byCategory.TryGetValue(categoryName, out List<Element> list))
                {
                    list = new List<Element>();
                    byCategory[categoryName] = list;
                    groups.Add(new KeyValuePair<string, List<Element>>(categoryName, list));
                }
                list.Add(el);
            }

            foreach (var group in groups)
            {
                string categoryName = group.Key;
                List<Element> elements = group.Value;
                items.Add(new
                {
                    id = "grp_" + categoryName,
                    name = WebUtility.HtmlEncode($"{categoryName} ({elements.Count})"),
                    isGroup = true,
                    isCollapsibleGroup = true,
                    hasChildren = false
                });

                foreach (Element el in elements)
                {
                    string elementName = ElementName(el, categoryName);
                    items.Add(new
                    {
                        id = SelectionMenuIds.Element(el.Id),
                        name = WebUtility.HtmlEncode($"{elementName} [{el.Id.GetIdValue()}]"),
                        hasChildren = true,
                        groupKey = categoryName
                    });
                }
            }
            return items;
        }

        static string ElementName(Element el, string fallback)
        {
            try
            {
                string name = el.Name;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
            }
            return fallback;
        }
    }
}
