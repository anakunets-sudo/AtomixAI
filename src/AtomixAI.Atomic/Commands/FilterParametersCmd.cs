using AtomixAI.Core;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using static AtomixAI.Atomic.BimKeys;

namespace AtomixAI.Atomic.Commands
{
    [AiInfo(
        name: "filter_parameters",
        group: AtomicGroupType.Search,
        description: "Advanced element filter evaluating exact parameter values, types, phases, or names using memory-efficient lazy iteration.",
        keywords: new[] { "parameter", "value", "thickness", "width", "length", "mark", "phase" })]
    public class FilterParametersCmd : BaseAtomicCommand
    {
        public class ParamFilterSchema : DynamicBimContract
        {
            [AiParam("The target parameter type to evaluate. Allowed values: 'Parameter', 'Phase'.", isRequired: true)]
            public string FilterType { get; set; }

            [AiParam("The exact Revit parameter name (Instance or Type), e.g., 'Width', 'Comments', 'Mark'. Required if FilterType is 'Parameter'.", isRequired: false)]
            public string ParameterName { get; set; }

            [AiParam("Comparison operator: '==', '!=', '>', '<', '>=', '<=', 'contains', not_contains', 'begins', 'not_begins', 'ends', 'not_ends', ' ", isRequired: true)]
            public string Operator { get; set; }

            [AiParam("The value to look for (e.g., '200mm', 'Option A', 'New Construction'). Supports dimension parsing.", isRequired: true)]
            public string Value { get; set; }
        }

        [AiParam(schema: typeof(ParamFilterSchema), type: "json", isRequired: true)]
        public override DynamicBimContract Params { get; set; }

        protected override AtomicResult Execute(ITransactionHandler handler)
        {
            UIDocument uidoc = handler.UIDoc;
            Autodesk.Revit.DB.Document doc = uidoc.Document;

            if(!GetInput(out DynamicBimContract dynamicBimContract).Success)
            {
                return AtomicResult.Error($"Failed to retrieve input tag '{In}' from AtomicStorage.");
            }

            dynamicBimContract.Get(BimKeys.Elements.ElementIds, out List<ElementId> inputIds);

            if (inputIds == null || inputIds.Count == 0)
            {
                return AtomicResult.Error($"Parameter filter skipped. Input collection is empty.");
            }

            // Perform single upcast to the parameter contract
            var myParams = this.Params as ParamFilterSchema;
            if (myParams == null)
            {
                return SetOutput(null, 0, false, "Critical Error: Invalid parameter contract layout.");
            }

            string filterType = myParams.FilterType ?? "Parameter";
            string op = myParams.Operator.ToLower();
            string aiValueStr = myParams.Value;
            string paramName = myParams.ParameterName;

            ICollection<ElementId> filteredIds = new List<ElementId>();

            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] START");

            if (filterType.ToLower() == "parameter")
            {
                filteredIds = ResolveParameterFilter(inputIds, paramName, op, aiValueStr, doc);
            }

            // 3. Fully universal cross-version empty state return via Exclude []
            if (filteredIds.Count == 0)
            {
                var emptyCollector = new FilteredElementCollector(doc, inputIds);
                return SetOutput(emptyCollector.Excluding(inputIds).ToList(), 0, false, "0 elements matched the parameter conditions.");
            }

            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] Processed from {inputIds.Count} down to {filteredIds.Count} items.");

            var contract = new DynamicBimContract();

            contract.Set(BimKeys.Elements.ElementIds, filteredIds.ToList());

            return SetOutput(contract, filteredIds.Count, true, $"Filtered {filteredIds.Count} parameter elements.");
        }

        private ICollection<ElementId> ResolveParameterFilter(List<ElementId> inputIds, string paramName, string op, string aiValueStr, Document doc)
        {
            ICollection<ElementId> resultIds = new List<ElementId>();

            var collector = new FilteredElementCollector(doc, inputIds);

            var param = FindElementsWithParameterUltraFast(doc, inputIds, paramName);

            if (param != null)
            {
                System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] param Id {param.Item1.Id}.");

                var provider = new ParameterValueProvider(param.Item1.Id);

                FilterRule rule = null;

                Autodesk.Revit.DB.FilterRule ruleBase = null;

                switch (param.Item1.StorageType)
                { /*
                    case StorageType.String:

                        rule = op switch
                        {
                            "contains" => ParameterFilterRuleFactory.CreateContainsRule(param.Id, aiValueStr),
                            "not_contains" => ParameterFilterRuleFactory.CreateNotContainsRule(param.Id, aiValueStr),
                            "begins" => ParameterFilterRuleFactory.CreateBeginsWithRule(param.Id, aiValueStr),
                            "not_begins" => ParameterFilterRuleFactory.CreateNotBeginsWithRule(param.Id, aiValueStr),
                            "ends" => ParameterFilterRuleFactory.CreateEndsWithRule(param.Id, aiValueStr),
                            "not_ends" => ParameterFilterRuleFactory.CreateNotEndsWithRule(param.Id, aiValueStr),
                            "==" => ParameterFilterRuleFactory.CreateEqualsRule(param.Id, aiValueStr),
                            "!=" => ParameterFilterRuleFactory.CreateNotEqualsRule(param.Id, aiValueStr),
                            _ => ParameterFilterRuleFactory.CreateEqualsRule(param.Id, aiValueStr)
                        };
                        break;
                    */
                    case StorageType.Double:
                        FilterNumericRuleEvaluator doubleEvaluator = op switch
                        {
                            ">" => new FilterNumericGreater(),
                            "<" => new FilterNumericLess(),
                            "<=" => new FilterNumericLessOrEqual(),
                            ">=" => new FilterNumericGreaterOrEqual(),
                            "==" => new FilterNumericEquals(),
                            "!=" => new FilterNumericEquals(),
                            _ => new FilterNumericEquals(),
                        };

                        double valDouble = param.Item1.ParseDouble(aiValueStr);
                        ruleBase = new FilterDoubleRule(provider, doubleEvaluator, valDouble, 0.001);
                        rule = (op == "!=") ? new FilterInverseRule(ruleBase) : ruleBase;
                        break;

                    case StorageType.Integer:
                        FilterNumericRuleEvaluator intEvaluator = op switch
                        {
                            ">" => new FilterNumericGreater(),
                            "<" => new FilterNumericLess(),
                            "<=" => new FilterNumericLessOrEqual(),
                            ">=" => new FilterNumericGreaterOrEqual(),
                            "==" => new FilterNumericEquals(),
                            "!=" => new FilterNumericEquals(),
                            _ => new FilterNumericEquals(),
                        };
                        int valInt = Convert.ToInt32(param.Item1.ParseDouble(aiValueStr)); ; // Например "1" для Boolean True
                        ruleBase = new FilterIntegerRule(provider, intEvaluator, valInt);
                        rule = (op == "!=") ? new FilterInverseRule(ruleBase) : ruleBase;
                        break;

                    case StorageType.ElementId:

                        int valId = int.Parse(aiValueStr);
                        // Если ИИ ищет по Уровню, он должен передать Id этого уровня
                        FilterNumericRuleEvaluator idEvaluator = new FilterNumericEquals();
                        ElementId idVal = new ElementId(valId);
                        ruleBase = new FilterElementIdRule(provider, idEvaluator, idVal);
                        rule = (op == "!=") ? new FilterInverseRule(ruleBase) : ruleBase;
                        break;
                }

                if (param.Item1.StorageType != StorageType.String)
                {
                    resultIds = collector.WherePasses(new ElementParameterFilter(rule)).ToElementIds();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] collector count {collector.GetElementCount()}.");

                    bool isType = param.Item2;

                    var filteredElements = collector.ToElements().Where(el =>
                    {
                        BuiltInParameter bip;
#if REVIT2024_OR_GREATER
                        bip = (BuiltInParameter)param.Item1.Id.Value;
#else
                        bip = (BuiltInParameter)param.Item1.Id.IntegerValue;
#endif
                        // Достаем значение параметра экземпляра или типа
                        string actualValue = (isType ? doc.GetElement(el.GetTypeId())?.get_Parameter(bip)?.AsString() 
                            : el.get_Parameter(bip)?.AsString()) ?? string.Empty;

                        // Применяем регистронезависимое сравнение строк средствами .NET
                        return op switch
                        {
                            "contains" => actualValue.Contains(aiValueStr, StringComparison.OrdinalIgnoreCase),
                            "not_contains" => !actualValue.Contains(aiValueStr, StringComparison.OrdinalIgnoreCase),
                            "begins" => actualValue.StartsWith(aiValueStr, StringComparison.OrdinalIgnoreCase),
                            "not_begins" => !actualValue.StartsWith(aiValueStr, StringComparison.OrdinalIgnoreCase),
                            "ends" => actualValue.EndsWith(aiValueStr, StringComparison.OrdinalIgnoreCase),
                            "not_ends" => !actualValue.EndsWith(aiValueStr, StringComparison.OrdinalIgnoreCase),
                            "==" => actualValue.Equals(aiValueStr, StringComparison.OrdinalIgnoreCase),
                            "!=" => !actualValue.Equals(aiValueStr, StringComparison.OrdinalIgnoreCase),
                            _ => actualValue.Equals(aiValueStr, StringComparison.OrdinalIgnoreCase)
                        };
                    });

                    resultIds = filteredElements.Select(el => el.Id).ToList();
                }
            }

            return resultIds;
        }


        public Tuple<Parameter, bool> FindElementsWithParameterUltraFast(Autodesk.Revit.DB.Document doc, List<ElementId> inputIds, string parameterName)
        {
            if (inputIds == null || inputIds.Count == 0) return null;

            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] [FindElementsWithParameterUltraFast] parameterName {parameterName}.");

            // БЫСТРЫЙ ПУТЬ: Проверяем самый первый элемент выборки
            var firstElement = doc.GetElement(inputIds.First());
            if (firstElement == null) return null;

            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] [FindElementsWithParameterUltraFast] first Element {firstElement.Name}.");

            // Вытаскиваем ID категории первого элемента максимально дешево (через встроенный параметр)
            ElementId firstElementCategoryId = firstElement.get_Parameter(BuiltInParameter.ELEM_CATEGORY_PARAM)?.AsElementId()
                                               ?? ElementId.InvalidElementId;

            var p = GetParameterByEnglishName(parameterName, firstElement.Parameters);

            if(p != null) return new Tuple<Parameter, bool>(p,false);

            ElementId firstTypeId = firstElement.GetTypeId();
            if (firstTypeId != ElementId.InvalidElementId)
            {
                Element typeEl = doc.GetElement(firstTypeId);

                p = GetParameterByEnglishName(parameterName, typeEl.Parameters);

                if (p != null) return new Tuple<Parameter, bool>(p, true);
            }

            // 1. Создаем коллектор ИСКЛЮЧИТЕЛЬНО по входящим ID.
            // Элементы в память .NET еще НЕ выгружены!
            var mainCollector = new FilteredElementCollector(doc, inputIds);

            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] [FindElementsWithParameterUltraFast] mainCollector Count {mainCollector.GetElementCount()}.");

            // Провайдер для системного параметра категории
            var catParamId = new ElementId(BuiltInParameter.ELEM_CATEGORY_PARAM);
            var provider = new ParameterValueProvider(catParamId);

            // Самый дешевый способ вытащить категории через ELEM_CATEGORY_PARAM:

            var uniqueCategoryIds = mainCollector
                .Select(el => provider.GetElementIdValue(el))?
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .Distinct()
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] [FindElementsWithParameterUltraFast] uniqueCategoryIds Count {uniqueCategoryIds.Count}.");

            // 3. А теперь наш любимый быстрый цикл с прерыванием (break) по найденным категориям!
            foreach (ElementId catId in uniqueCategoryIds)
            {
                if (catId != firstElementCategoryId)
                {
                    // Создаем микро-коллектор только для этой категории внутри нашей выборки inputIds
                    var catCollector = new FilteredElementCollector(doc, inputIds)
                        .OfCategoryId(catId);

                    // Забираем ровно ОДИН элемент этой категории из C++ ядра (это ультра-дешево)
                    Element sampleElement = catCollector.FirstElement();
                    if (sampleElement == null) continue;

                    // Ищем параметр в экземпляре
                    var param = GetParameterByEnglishName(parameterName, sampleElement.Parameters);

                    if (param != null) return new Tuple<Parameter, bool>(param, false);

                    // Если в экземпляре нет, проверяем Тип
                    ElementId typeId = sampleElement.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        Element typeEl = doc.GetElement(typeId);

                        param = GetParameterByEnglishName(parameterName, typeEl.Parameters);

                        if (param != null) return new Tuple<Parameter, bool>(param, true);
                    }
                }
            }

            return null;
        }

        private Parameter GetParameterByEnglishName(string parameterName, ParameterSet set)
        {
            foreach (Parameter p in set)
            {
                string englishNameFromDefinition;

                // Заставляем Revit сказать, как этот параметр называется на АНГЛИЙСКОМ 
                // (даже если у пользователя сейчас немецкий или русский Revit)
#if REVIT2024_OR_GREATER
                englishNameFromDefinition = LabelUtils.GetLabelFor((BuiltInParameter) p.Id.Value, LanguageType.English_USA);
#else
                englishNameFromDefinition = LabelUtils.GetLabelFor((BuiltInParameter) p.Id.IntegerValue, LanguageType.English_USA);
#endif
                System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] [FindElementsWithParameterUltraFast] englishNameFromDefinition {englishNameFromDefinition}.");
                // Если английское имя из внутренностей Revit совпало с тем, что прислал ИИ ("Width")
                if (englishNameFromDefinition.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] [FindElementsWithParameterUltraFast] Equals!");
                    return p; // Мгновенно возвращаем этот параметр!
                }
            }

            return null;
        }
    }
}
