using AtomixAI.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection.Metadata;

namespace AtomixAI.UI.Commands.Selection
{
    public static class ParameterMenuBuilder
    {
        internal static object? BuildBuiltInParameterItem(Element originalElement, BuiltInParameter builtInParameter, bool isType)
        {
            Element paramHolder = originalElement;
            if (isType)
            {
                ElementId typeId = originalElement.GetTypeId();
                if (typeId == ElementId.InvalidElementId) return null;
                paramHolder = originalElement.Document.GetElement(typeId);
                if (paramHolder == null) return null;
            }

            Autodesk.Revit.DB.Parameter? parameter = paramHolder.get_Parameter(builtInParameter);
            return parameter?.Definition == null
                ? null
                : BuildParameterItem(paramHolder, parameter, originalElement, isType);
        }

        private static object BuildParameterItem(Element paramHolder, Autodesk.Revit.DB.Parameter parameter, Element originalElement, bool isType)
        {
            string name = parameter.Definition!.Name;
            string value = GetDisplayValue(parameter);
            string label = string.IsNullOrEmpty(value) ? name : $"{name}: {value}";
            return new
            {
                id = BuildParameterItemId(paramHolder, parameter, originalElement, isType),
                name = WebUtility.HtmlEncode(label),
                searchText = name,
                hasChildren = false,
                isParameterChip = true,
                isMenuParameterBranch = true
            };
        }

        internal static object? BuildNameItem(Element originalElement)
        {
            if (originalElement == null || string.IsNullOrEmpty(originalElement.Name)) return null;

            string value = originalElement.Name;
            string label = $"{value}";
            return new
            {
                id = $"name:{originalElement.Name}:{originalElement.Id.GetIdValue()}:{0}",
                name = WebUtility.HtmlEncode(label),
                searchText = label,
                hasChildren = false,
                isParameterChip = true,
                isMenuParameterBranch = true
            };
        }

        internal static object? BuildIdItem(Element originalElement)
        {
            if (originalElement == null || originalElement.Id == ElementId.InvalidElementId) return null;

            string name = "Id";
            string value = originalElement.Id.GetIdValue().ToString();
            string label = string.IsNullOrEmpty(value) ? name : $"{name}: {value}";
            return new
            {
                id = $"id:{originalElement.Name}:{originalElement.Id.GetIdValue()}:{0}",
                name = WebUtility.HtmlEncode(label),
                searchText = label,
                hasChildren = false,
                isParameterChip = true,
                isMenuParameterBranch = true
            };
        }

        public static List<object> Build(Element paramHolder, bool isType, Element originalElement)
        {
            var items = new List<object>();
            var groups = new List<ParameterMenuGroup>();
            var groupsById = new Dictionary<string, ParameterMenuGroup>(StringComparer.Ordinal);

            var allBipNames = new List<string>();

            foreach (Autodesk.Revit.DB.Parameter parameter in paramHolder.Parameters)
            {
                string? name = parameter.Definition?.Name;
                if (string.IsNullOrEmpty(name)) continue;

                if( parameter.IsReadOnly &&
                    string.IsNullOrEmpty(parameter.AsValueString()) 
                    && string.IsNullOrEmpty(parameter.AsString())
                    && parameter.AsElementId() == ElementId.InvalidElementId
                    && parameter.AsDouble() == 0 
                    && parameter.AsInteger() == 0) continue;

                bool isIdLessZero = false;

#if REVIT2024_OR_GREATER 
                isIdLessZero = parameter.Id.Value < 0;
#else 
                isIdLessZero = parameter.Id.IntegerValue < 0;
#endif
                if (isIdLessZero && allBipNames.Contains(name)) continue;

                allBipNames.Add(name);

                string value = GetDisplayValue(parameter);
                string label = string.IsNullOrEmpty(value) ? name : $"{name}: {value}";
                GetParameterGroup(parameter.Definition!, out string groupId, out string groupName);

                if (!groupsById.TryGetValue(groupId, out ParameterMenuGroup group))
                {
                    group = new ParameterMenuGroup(groupId, groupName);
                    groupsById.Add(groupId, group);
                    groups.Add(group);
                }

                group.Items.Add(new
                {
                    id = BuildParameterItemId(paramHolder, parameter, originalElement, isType),
                    name = label,
                    searchText = name,
                    hasChildren = false,
                    isParameterChip = true,
                    groupKey = groupId
                });
            }

            allBipNames = null;

            // Группы — по имени, внутри — уже по имени параметра.
            groups.Sort((a, b) =>
            {
                int byName = StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
                return byName != 0 ? byName : StringComparer.Ordinal.Compare(a.Id, b.Id);
            });

            foreach (ParameterMenuGroup group in groups)
            {
                items.Add(new
                {
                    id = $"param_group_{group.Id}",
                    name = WebUtility.HtmlEncode($"{group.Name} ({group.Items.Count})"),
                    isGroup = true,
                    isCollapsibleGroup = true,
                    hasChildren = false
                });
                items.AddRange(group.Items);
            }

            return items;
        }

        private static void GetParameterGroup(Definition definition, out string groupId, out string groupName)
        {
#if REVIT2024_OR_GREATER
            ForgeTypeId groupTypeId = definition.GetGroupTypeId();
            groupId = groupTypeId.TypeId;
            groupName = LabelUtils.GetLabelForGroup(groupTypeId);
#else
            BuiltInParameterGroup groupType = definition.ParameterGroup;
            groupId = ((int)groupType).ToString(System.Globalization.CultureInfo.InvariantCulture);
            groupName = LabelUtils.GetLabelFor(groupType);
#endif
        }

        private sealed class ParameterMenuGroup
        {
            public string Id { get; }
            public string Name { get; }
            public List<object> Items { get; } = new List<object>();

            public ParameterMenuGroup(string id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        internal static ChipPayload CreateChip(UIApplication app, string itemId)
        {
            string[] parts = itemId.Split(':');
            if (parts.Length != 5 || parts[0] != "param" ||
                !long.TryParse(parts[1], out long holderId) ||
                !long.TryParse(parts[2], out long parameterId) ||
                !long.TryParse(parts[3], out long elementId) ||
                (parts[4] != "0" && parts[4] != "1"))
                throw new ArgumentException("Invalid parameter chip selection.");

            Autodesk.Revit.DB.Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) throw new ArgumentException("No active Revit document found.");

            Element? holder = doc.GetElement(ToElementId(holderId));
            Element? originalElement = doc.GetElement(ToElementId(elementId));
            Autodesk.Revit.DB.Parameter? parameter = null;
            if (holder != null)
            {
                foreach (Autodesk.Revit.DB.Parameter candidate in holder.Parameters)
                {
                    if (candidate.Id.GetIdValue() == parameterId)
                    {
                        parameter = candidate;
                        break;
                    }
                }
            }
            if (holder == null || originalElement == null || parameter?.Definition == null)
                throw new ArgumentException("The selected parameter is no longer available. Select it again from the menu.");

            bool isType = parts[4] == "1";
            JObject data = BuildData(doc, parameter, holder, originalElement, isType);
            string name = parameter.Definition.Name;
            string value = GetDisplayValue(parameter);
            string label = BuildChipLabel(name, value);
            string chipId = ChipStorage.Shared.CreatePending(label, data);
            return new ChipPayload(chipId, label, data);
        }

        // Подпись чипса: "#<Name>" для пустого значения, иначе "#<Name>: <Value>".
        private static string BuildChipLabel(string name, string value) =>
            string.IsNullOrEmpty(value) ? $"{name}" : $"{name}: {value}";

        private static JObject BuildData(Autodesk.Revit.DB.Document doc, Autodesk.Revit.DB.Parameter parameter, Element paramHolder, Element originalElement, bool isType)
        {
            string name = parameter.Definition!.Name;
            string value = GetDisplayValue(parameter);
            string unitLabel = string.Empty;
            string displayUnitId = string.Empty;

            if (parameter.HasValue && parameter.StorageType == StorageType.Double)
            {
                try
                {
                    ForgeTypeId dataType = parameter.Definition.GetDataType();
                    if (dataType != null && UnitUtils.IsMeasurableSpec(dataType))
                    {
                        ForgeTypeId unitTypeId = parameter.GetUnitTypeId();
                        if (unitTypeId != null && !string.IsNullOrEmpty(unitTypeId.TypeId))
                        {
                            displayUnitId = unitTypeId.TypeId;
                            unitLabel = GetShortUnitLabel(doc, unitTypeId);
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    displayUnitId = string.Empty;
                    unitLabel = string.Empty;
                }
            }

            var data = JObject.FromObject(new
            {
                kind = "revit_parameter",
                parameter_Name = name,
                parameter_Value = value,
                parameter_Unit_Label = unitLabel,
                parameter_Storage_Type = parameter.StorageType.ToString(),
                parameter_Is_Type = isType,
                parameter_has_Value = parameter.HasValue,
                parameter_is_ReadOnly = parameter.IsReadOnly,
                parameter_Id = parameter.Id.GetIdValue().ToString(),
                element_Id = originalElement.Id.GetIdValue().ToString(),
                parameter_Is_Shared = parameter.IsShared,
                parameter_Guid = parameter.IsShared ? parameter.GUID.ToString() : string.Empty,
                parameter_Holder_Id = paramHolder.Id.GetIdValue().ToString(),
                element_Name = originalElement.Name,
                element_Сategory = originalElement.Category?.Name ?? Localizer.T("selection.noCategory"),
                document_Title = originalElement.Document.Title,
                document_Path = originalElement.Document.PathName
            });

            if (parameter.HasValue)
            {
                switch (parameter.StorageType)
                {
                    case StorageType.Double:
                        data["value"] = parameter.AsDouble();
                        ForgeTypeId dataType = parameter.Definition.GetDataType();
                        data["dataType"] = dataType.TypeId;
                        data["valueUnits"] = UnitUtils.IsMeasurableSpec(dataType) ? "revit_internal" : "unitless";
                        if (!string.IsNullOrEmpty(displayUnitId)) data["displayUnitId"] = displayUnitId;
                        break;
                    case StorageType.Integer:
                        data["value"] = parameter.AsInteger();
                        break;
                    case StorageType.String:
                        data["value"] = parameter.AsString();
                        break;
                    case StorageType.ElementId:
                        data["value"] = parameter.AsElementId().GetIdValue();
                        break;
                }
            }

            return data;
        }

        private static string GetDisplayValue(Autodesk.Revit.DB.Parameter parameter)
        {
            if (!parameter.HasValue) return string.Empty;
            string? value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
            if (string.IsNullOrEmpty(value) && parameter.StorageType == StorageType.Double)
                value = parameter.AsDouble().ToString();
            return value ?? string.Empty;
        }

        private static string BuildParameterItemId(Element holder, Autodesk.Revit.DB.Parameter parameter, Element originalElement, bool isType) =>
            $"param:{holder.Id.GetIdValue()}:{parameter.Id.GetIdValue()}:{originalElement.Id.GetIdValue()}:{(isType ? 1 : 0)}";

        private static ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId(checked((int)id));
#endif
        }

        private static string GetShortUnitLabel(Autodesk.Revit.DB.Document doc, ForgeTypeId unitTypeId)
        {
            if (unitTypeId == null || string.IsNullOrEmpty(unitTypeId.TypeId))
                return string.Empty;

            try
            {
                if (FormatOptions.CanHaveSymbol(unitTypeId))
                {
                    System.Collections.Generic.IList<ForgeTypeId> validSymbols = FormatOptions.GetValidSymbols(unitTypeId);

                    // ФИКС: Пропускаем пустые "символы-пустышки" (у которых TypeId пустой)
                    // Берем только тот ForgeTypeId, который реально имеет определение внутри Revit
                    ForgeTypeId? defaultSymbolId = validSymbols.FirstOrDefault(s => s != null && !string.IsNullOrEmpty(s.TypeId));

                    if (defaultSymbolId != null)
                    {
                        // Теперь сюда никогда не попадет пустой ID, и ошибки не будет
                        string shortLabel = LabelUtils.GetLabelForSymbol(defaultSymbolId);

                        System.Diagnostics.Debug.WriteLine($"[ParameterMenuBuilder] [GetShortUnitLabel] shortLabel {shortLabel}.");
                        return shortLabel;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParameterMenuBuilder] [GetShortUnitLabel] Exception: {ex.Message}");
                return string.Empty;
            }
        }


    }
}
