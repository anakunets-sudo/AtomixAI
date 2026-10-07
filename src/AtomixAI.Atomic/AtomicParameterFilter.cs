using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;

namespace AtomixAI.Atomic
{
    public static class AtomicParameterFilter
    {
        public static FilteredElementCollector Execute(
            FilteredElementCollector collector,
            List<Dictionary<string, object>> parameterInstructions,
            Document doc)
        {
            if (collector == null) return null;
            if (parameterInstructions == null || parameterInstructions.Count == 0) return collector;

            List<ElementId> filteredIds = new List<ElementId>();

            // Оптимизация 1: Итерируемся прямо по коллектору Revit. Никаких .ToElementIds().ToList()!
            // Это экономит память и процессорное время.
            foreach (Element elem in collector)
            {
                if (elem == null) continue;

                bool passesAllConditions = true;

                // Логика AND: Элемент должен пройти ВСЕ условия
                foreach (var inst in parameterInstructions)
                {
                    if (!inst.TryGetValue("ParameterName", out var rawName) || rawName == null) continue;
                    if (!inst.TryGetValue("Operator", out var rawOp) || rawOp == null) continue;
                    if (!inst.TryGetValue("Value", out var rawVal) || rawVal == null) continue;

                    string paramName = rawName.ToString();
                    string op = rawOp.ToString();
                    string aiValueStr = rawVal.ToString();

                    // 1. Ищем параметр на Экземпляре, при неуспехе — в Типе
                    Parameter parameter = elem.LookupParameter(paramName);
                    if (parameter == null || !parameter.HasValue)
                    {
                        ElementId typeId = elem.GetTypeId();
                        if (typeId != ElementId.InvalidElementId)
                        {
                            Element typeElem = doc.GetElement(typeId);
                            parameter = typeElem?.LookupParameter(paramName);
                        }
                    }

                    if (parameter == null || !parameter.HasValue)
                    {
                        passesAllConditions = false;
                        break;
                    }

                    // 2. Выполняем сравнение на основе РЕАЛЬНОГО типа хранения в Revit
                    bool conditionResult = false;

                    switch (parameter.StorageType)
                    {
                        case StorageType.Double:
                            // Динамически определяем, ЧТО это за дабл (Длина, Угол и т.д.)
                            double aiDoubleValue = ParseDoubleByParameterType(parameter, aiValueStr, doc);
                            double revitDoubleValue = parameter.AsDouble();
                            conditionResult = CompareDoubles(revitDoubleValue, aiDoubleValue, op);
                            break;

                        case StorageType.Integer:
                            // Безопасное приведение целых чисел (параметры да/нет, энумы, ID категорий)
                            int revitIntValue = parameter.AsInteger();
                            if (int.TryParse(aiValueStr, out int aiIntValue))
                            {
                                conditionResult = CompareIntegers(revitIntValue, aiIntValue, op);
                            }
                            else
                            {
                                // Если ИИ прислал текст ("Да"/"Нет") для Integer-параметра
                                string displayValue = parameter.AsValueString() ?? revitIntValue.ToString();
                                conditionResult = CompareStrings(displayValue, aiValueStr, op);
                            }
                            break;

                        case StorageType.String:
                        default:
                            string revitStringValue = parameter.AsString() ?? parameter.AsValueString() ?? string.Empty;
                            conditionResult = CompareStrings(revitStringValue, aiValueStr, op);
                            break;
                    }

                    if (!conditionResult)
                    {
                        passesAllConditions = false;
                        break;
                    }
                }

                if (passesAllConditions)
                {
                    filteredIds.Add(elem.Id);
                }
            }

            // Если ничего не нашли — возвращаем пустой коллектор безопасным для старых/новых версий путем
            if (filteredIds.Count == 0)
            {
#if REVIT2024_OR_GREATER
            return new FilteredElementCollector(doc).WherePasses(new ElementIdSetFilter(new List<ElementId>()));
#else
                // Для Revit 2019-2023: Инициализируем коллектор по пустому списку ID. 
                // Это мгновенно вернет пустой коллектор без генерации исключений.
                return new FilteredElementCollector(doc, new List<ElementId>());
#endif
            }

            // Возвращаем отфильтрованные элементы
#if REVIT2024_OR_GREATER
        return new FilteredElementCollector(doc).WherePasses(new ElementIdSetFilter(filteredIds));
#else
            // Для Revit 2019-2023: Передаем отфильтрованную коллекцию ID прямо в конструктор коллектора.
            // Это самый быстрый и нативный способ сузить выборку в старом API.
            return new FilteredElementCollector(doc, filteredIds);
#endif
        }

        private static double ParseDoubleByParameterType(Parameter parameter, string aiValue, Document doc)
        {
            // Умный парсинг Double в зависимости от назначения параметра
#if REVIT2022_OR_GREATER
        ForgeTypeId dataType = parameter.Definition.GetDataType();
        if (dataType == SpecTypeId.Angle)
        {
            return BimUnitConverter.ParseAngle(aiValue, doc);
        }
        // Здесь можно расширить для SpecTypeId.Area, SpecTypeId.Volume, если ваш Util это поддерживает
        return BimUnitConverter.ParseLength(aiValue); 
#else
            // Старое API Ревита
            if (parameter.Definition.ParameterType == ParameterType.Angle)
            {
                return BimUnitConverter.ParseAngle(aiValue, doc);
            }
            return BimUnitConverter.ParseLength(aiValue);
#endif
        }

        private static bool CompareDoubles(double revitVal, double aiVal, string op)
        {
            double tolerance = 0.001; // Погрешность для футов
            switch (op)
            {
                case "==": return Math.Abs(revitVal - aiVal) < tolerance;
                case ">": return (revitVal - aiVal) > tolerance;
                case "<": return (aiVal - revitVal) > tolerance;
                case ">=": return (revitVal - aiVal) >= -tolerance;
                case "<=": return (aiVal - revitVal) >= -tolerance;
                case "!=": return Math.Abs(revitVal - aiVal) >= tolerance;
                default: return false;
            }
        }

        private static bool CompareIntegers(int revitVal, int aiVal, string op)
        {
            switch (op)
            {
                case "==": return revitVal == aiVal;
                case ">": return revitVal > aiVal;
                case "<": return revitVal < aiVal;
                case ">=": return revitVal >= aiVal;
                case "<=": return revitVal <= aiVal;
                case "!=": return revitVal != aiVal;
                default: return false;
            }
        }

        private static bool CompareStrings(string revitVal, string aiVal, string op)
        {
            revitVal ??= string.Empty;
            aiVal ??= string.Empty;

            switch (op.ToLower())
            {
                case "==": return revitVal.Equals(aiVal, StringComparison.OrdinalIgnoreCase);
                case "!=": return !revitVal.Equals(aiVal, StringComparison.OrdinalIgnoreCase);
                case "contains": return revitVal.IndexOf(aiVal, StringComparison.OrdinalIgnoreCase) >= 0;
                default: return false;
            }
        }
    }
}
