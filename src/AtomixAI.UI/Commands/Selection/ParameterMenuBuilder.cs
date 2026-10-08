using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AtomixAI.Core;

namespace AtomixAI.UI.Commands.Selection
{
    // Строки параметров + meta для чипса (chips.js)
    public static class ParameterMenuBuilder
    {
        public static List<object> Build(Element paramHolder, bool isType, Element originalElement)
        {
            var items = new List<object>();

            string elId = originalElement.Id.GetIdValue().ToString();
            string elCategory = originalElement.Category?.Name ?? Localizer.T("selection.noCategory");
            string elName = originalElement.Name;
            string paramBinding = Localizer.T(isType ? "selection.typeParam" : "selection.instanceParam");

            // GetOrderedParameters — только параметры Properties palette (видимые юзеру в Revit UI)
            foreach (Parameter p in paramHolder.GetOrderedParameters())
            {
                string? name = p.Definition?.Name;
                if (string.IsNullOrEmpty(name)) continue;

                if (p.Definition is InternalDefinition internalDef && !internalDef.Visible)
                    continue;

                string val = "";
                if (p.HasValue)
                {
                    val = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                    if (string.IsNullOrEmpty(val) && p.StorageType == StorageType.Double)
                        val = p.AsDouble().ToString();
                    val ??= "";
                }

                string label = string.IsNullOrEmpty(val) ? name : $"{name}: {val}";
                items.Add(new
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
                });
            }

            return items;
        }
    }
}
