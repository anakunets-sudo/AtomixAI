using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AtomixAI.Core;

namespace AtomixAI.UI.Commands.Selection
{
    // Строки параметров + meta для чипса (chips.js)
    public static class ParameterMenuBuilder
    {
        public static List<object> Build(Element paramHolder, bool isType, Element originalElement)
        {
            var items = new List<(string Name, object Item)>();

            string elId = originalElement.Id.GetIdValue().ToString();
            string elCategory = originalElement.Category?.Name ?? Localizer.T("selection.noCategory");
            string elName = originalElement.Name;
            string paramBinding = Localizer.T(isType ? "selection.typeParam" : "selection.instanceParam");

            foreach (Parameter p in paramHolder.Parameters)
            {
                if (!p.HasValue) continue;

                string? name = p.Definition?.Name;
                if (string.IsNullOrEmpty(name)) continue;

                string val = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                if (string.IsNullOrEmpty(val)) val = p.AsDouble().ToString();

                string label = $"{name}: {val}";
                items.Add((label, new
                {
                    id = $"param_{Guid.NewGuid()}",
                    name = label,
                    hasChildren = false,
                    meta = new
                    {
                        paramName = name,
                        paramValue = val,
                        paramType = paramBinding,
                        elementId = elId,
                        elementName = elName,
                        category = elCategory
                    }
                }));
            }

            return items.OrderBy(i => i.Name).Select(i => i.Item).ToList();
        }
    }
}
