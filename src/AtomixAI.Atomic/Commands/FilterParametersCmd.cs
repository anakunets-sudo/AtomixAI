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

            [AiParam("The exact Revit parameter ID (for an instance or type), for example: '-1010101'.", isRequired: false)]
            public string ParameterId { get; set; }

            [AiParam("The exact Revit parameter GUID of a shared parameter (for an instance or type), for example: 'A6462D5F-670D-43F6-900B-7E2717986EAD'. Leave empty for non-shared parameters.", isRequired: false)]
            public string ParameterGuid { get; set; }

            [AiParam("If the parameter is a type then the value is 'true'. If the parameter is an instance then the value is 'false'.", isRequired: false)]
            public string IsType { get; set; }

            [AiParam("Comparison operator: '==', '!=', '>', '<', '>=', '<=', 'contains', not_contains', 'begins', 'not_begins', 'ends', 'not_ends', ' ", isRequired: true)]
            public string Operator { get; set; }

            [AiParam("The value to look for (e.g., '200mm', 'Option A', 'New Construction'). Supports dimension parsing.", isRequired: true)]
            public string ParameterValue { get; set; }
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

            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] Params.FilterType: {(string.IsNullOrEmpty(myParams.FilterType) ? "null/empty" : myParams.FilterType)}");
            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] Params.ParameterName: {(string.IsNullOrEmpty(myParams.ParameterName) ? "null/empty" : myParams.ParameterName)}");
            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] Params.ParameterId: {(string.IsNullOrEmpty(myParams.ParameterId) ? "null/empty" : myParams.ParameterId)}");
            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] Params.ParameterGuid: {(string.IsNullOrEmpty(myParams.ParameterGuid) ? "null/empty" : myParams.ParameterGuid)}");
            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] Params.IsType: {(string.IsNullOrEmpty(myParams.IsType) ? "null/empty" : myParams.IsType)}");
            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] Params.Operator: {(string.IsNullOrEmpty(myParams.Operator) ? "null/empty" : myParams.Operator)}");
            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] Params.ParameterValue: {(string.IsNullOrEmpty(myParams.ParameterValue) ? "null/empty" : myParams.ParameterValue)}");

            string filterType = myParams.FilterType ?? "Parameter";            

            ICollection<ElementId> filteredIds = new List<ElementId>();

            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] START");

            if (filterType.ToLower() == "parameter")
            {
                filteredIds = ResolveParameterFilter(inputIds, myParams, doc);
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

        private ICollection<ElementId> ResolveParameterFilter(List<ElementId> inputIds, ParamFilterSchema myParams, Document doc)
        {
            string op = myParams.Operator.ToLower();

            string aiValueStr = myParams.ParameterValue;

            ICollection<ElementId> resultIds = new List<ElementId>();

            var collector = new FilteredElementCollector(doc, inputIds);

            var param = FindElementsWithParameterUltraFast(doc, inputIds, myParams);

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
                            "contains" => actualValue.IndexOf(aiValueStr, StringComparison.OrdinalIgnoreCase) >= 0,
                            "not_contains" => actualValue.IndexOf(aiValueStr, StringComparison.OrdinalIgnoreCase) < 0,
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


        public Tuple<Parameter, bool>? FindElementsWithParameterUltraFast(Autodesk.Revit.DB.Document doc, List<ElementId> inputIds, ParamFilterSchema myParams)
        {
            var parameterName = myParams.ParameterName;

            if (inputIds == null || inputIds.Count == 0) return null;

            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] [FindElementsWithParameterUltraFast] parameterName {parameterName}.");

            // БЫСТРЫЙ ПУТЬ: Проверяем самый первый элемент выборки
            var firstElement = doc.GetElement(inputIds.First());
            if (firstElement == null) return null;

            System.Diagnostics.Debug.WriteLine($"[PARAM_FILTER] [FindElementsWithParameterUltraFast] first Element {firstElement.Name}.");

            ElementId firstTypeId;

            long parameterId = long.TryParse(myParams.ParameterId, out long id) ? id : 0;

            bool? isTypeParameter = bool.TryParse(myParams.IsType, out bool result) ? result : null;

            if (parameterId < -1)
            {
                if (isTypeParameter.HasValue && isTypeParameter.Value == false)
                {
                    Parameter param = null;

#if REVIT2024_OR_GREATER
                    param = firstElement.get_Parameter((BuiltInParameter)parameterId);
#else
                    int paramIdInt = (int)parameterId;
                    param = firstElement.get_Parameter(paramIdInt);
#endif
                    if (param != null) return new Tuple<Parameter, bool>(param, false);
                }
                else if (isTypeParameter.HasValue && isTypeParameter.Value)
                {
                    firstTypeId = firstElement.GetTypeId();
                    if (firstTypeId != ElementId.InvalidElementId)
                    {
                        Element typeEl = doc.GetElement(firstTypeId);

                        Parameter param = null;

                        int paramIdInt = 0;

#if REVIT2024_OR_GREATER
                        param = typeEl.get_Parameter((BuiltInParameter)parameterId);
#else
                        int paramIdInt = (int)parameterId;
                        param = typeEl.get_Parameter((BuiltInParameter)paramIdInt);
#endif

                        if (param != null) return new Tuple<Parameter, bool>(param, true);
                    }
                }
                
                // Если ИИ прислал конкретный ParameterId, то ищем его напрямую
                var param_ = firstElement.get_Parameter((BuiltInParameter)parameterId);
                
                if (param_ != null) return new Tuple<Parameter, bool>(param_, false);
                // Если в экземпляре нет, проверяем Тип
                firstTypeId = firstElement.GetTypeId();
                if (firstTypeId != ElementId.InvalidElementId)
                {
                    Element typeEl = doc.GetElement(firstTypeId);
                    param_ = typeEl.get_Parameter((BuiltInParameter)parameterId);
                    if (param_ != null) return new Tuple<Parameter, bool>(param_, true);
                }
            }
            else if(parameterId > 0)
            {
                Guid paramGuid = Guid.TryParse(myParams.ParameterGuid, out Guid guid) ? guid : Guid.Empty;

                if (paramGuid != Guid.Empty)
                {
                    if (isTypeParameter.HasValue && isTypeParameter.Value == false)
                    {
                        var param = firstElement.get_Parameter(paramGuid);
                        if (param != null) return new Tuple<Parameter, bool>(param, false);
                    }
                    else if (isTypeParameter.HasValue && isTypeParameter.Value)
                    {
                        firstTypeId = firstElement.GetTypeId();
                        if (firstTypeId != ElementId.InvalidElementId)
                        {
                            Element typeEl = doc.GetElement(firstTypeId);
                            var param = typeEl.get_Parameter(paramGuid);
                            if (param != null) return new Tuple<Parameter, bool>(param, true);
                        }
                    }
                }
            }

            // Вытаскиваем ID категории первого элемента максимально дешево (через встроенный параметр)
            ElementId firstElementCategoryId = firstElement.get_Parameter(BuiltInParameter.ELEM_CATEGORY_PARAM)?.AsElementId()
                                               ?? ElementId.InvalidElementId;

            var p = GetParameterByEnglishName(parameterName, firstElement.Parameters);

            if(p != null) return new Tuple<Parameter, bool>(p,false);

            firstTypeId = firstElement.GetTypeId();
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
                .Select(el => provider.GetElementIdValue(el))
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

        private Parameter? GetParameterByEnglishName(string parameterName, ParameterSet set)
        {
            foreach (Parameter p in set)
            {
                string englishNameFromDefinition;

                // Resolve built-in parameter labels in English on all supported Revit versions.
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
