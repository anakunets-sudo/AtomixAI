using AtomixAI.Atomic;
using AtomixAI.Atomic.Commands;
using AtomixAI.Core;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AtomixAI.Bridge
{
    /// <summary>
    /// Выполняет роль оркестратора и маршрутизатора запросов к различным подсистемам, 
    /// сервисам или агентам.Автоматизирует процесс выбора, валидации и безопасного
    /// вызова нужного инструмента в зависимости от контекста задачи.
    /// </summary>
    public class ToolDispatcher
    {
        private readonly Dictionary<string, Type> _csCommands;
        private readonly PyRevitLoader _pyLoader;
        private McpHost _mcpHost;

        public void RegisterHost(McpHost host) => _mcpHost = host;

        public ToolDispatcher(string scriptsPath)
        {
            _pyLoader = new PyRevitLoader(scriptsPath);

            // Сканируем сборку на наличие команд IAtomicCommand
            _csCommands = Assembly.GetAssembly(typeof(AtomicSearchFactory))
                .GetTypes()
                .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToDictionary(
                    t => t.GetCustomAttribute<AiInfoAttribute>()?.Name ?? t.Name,
                    t => t,
                    StringComparer.OrdinalIgnoreCase
                );

            Debug.WriteLine($"[DISPATCHER] Initialized. Commands found: {_csCommands.Count}");
        }

        /// <summary>
        /// Выполняет всю присланную от ИИ последовательность шагов в единой транзакции
        /// </summary>
        public AtomicResult DispatchSequence(string jsonSequence, int generation = 0)
        {
            Debug.WriteLine("[DISPATCHER] >>> Processing Sequence Batch (Single Result Mode)");

            var reportBuilder = new StringBuilder();
            var promptAccumulated = string.Empty;
            var isPromptOverride = false;
            var metadataCollector = new Dictionary<string, object>();
            int successCount = 0;

            try
            {
                // 1. Парсим шаги конвейера
                var steps = JsonConvert.DeserializeObject<List<SequenceStep>>(jsonSequence);
                if (steps == null || steps.Count == 0)
                    return AtomicResult.Error("Empty sequence received.");

                // 2. Выполняем через TransactionManager (он сделает один Rollback при Success = false)
                return TransactionManager.ExecuteSequence("AtomixAI Plan", () =>
                {
                    int count = 0;

                    foreach (var step in steps)
                    {
                        if (TransactionManager.IsCurrentSequenceCancelled)
                            return AtomicResult.Cancel();

                        // Вызываем одиночный диспетчер для конкретного шага
                        var stepResult = Dispatch(step.Tool, JsonConvert.SerializeObject(step.Arguments));

                        if (stepResult.PromptBehavior == PromptBehavior.Override)
                        {
                            promptAccumulated = $"\n{stepResult.Prompt}\n";
                            isPromptOverride = true;
                        }
                        else if (stepResult.PromptBehavior == PromptBehavior.Append)
                        {
                            promptAccumulated += $"\n{stepResult.Prompt}\n";
                        }

                        // Накапливаем текстовый отчет для Message
                        reportBuilder.AppendLine($"- step: \"{++count}\", bim tool: \"{step.Tool}\",  status: {(stepResult.Success ? "OK" : "FAILED")}, {stepResult.Message}");

                        if (!stepResult.Success)
                        {
                            // ПРЕРЫВАЕМ цепочку при первой же ошибке выполнения
                            var errorResult = new AtomicResult
                            {
                                Success = false,
                                Message = $"Sequence halted at step '{step.Tool}': {stepResult.Message}\nFull Log:\n{reportBuilder}"
                            };

                            errorResult.PromptOverride(AtomicResult.DefaultPrompt + AtomicResult.ErrorPrompt);
                            return errorResult;
                        }

                        // Собираем "Квитанцию" для Data (Deep Metadata) по Out-тегам
                        if (step.Arguments.TryGetValue("Out", out var tagObj) && tagObj != null)
                        {
                            string tag = tagObj.ToString();
                            metadataCollector[tag] = new
                            {
                                count = stepResult.Data, // Передаем Count или ID
                                tool = step.Tool
                            };
                        }

                        successCount++;
                    }

                    // 3. ФИНАЛЬНЫЙ СБОР: Если всё успешно, создаем итоговый результат
                    var finalResult = new AtomicResult
                    {
                        Success = true,
                        Message = $"Success.\n{reportBuilder}", //$"Successfully executed {successCount} steps.\n{reportBuilder}",
                        Data = new
                        {
                            total_success = successCount,
                            tags = metadataCollector,
                            status = "Completed"
                        }
                    };

                    if (isPromptOverride)
                    {
                        finalResult.PromptOverride(promptAccumulated);
                    }
                    else
                    {
                        finalResult.PromptOverride(AtomicResult.DefaultPrompt + AtomicResult.SuccessPrompt + promptAccumulated);
                    }

                    return finalResult;
                }, generation);
            }
            catch (Exception ex)
            {
                var finalResult = AtomicResult.Error($"Sequence Critical Error: {ex.Message}");
                finalResult.PromptOverride(AtomicResult.DefaultPrompt + AtomicResult.ErrorPrompt);
                return finalResult;
            }
        }

        public class SequenceStep
        {
            [JsonProperty("name")]
            public string Tool { get; set; }

            [JsonProperty("arguments")]
            public Dictionary<string, object> Arguments { get; set; }
        }
        /// <summary>
        /// Выполняет одиночную команду по её ID и наполняет свойства
        /// </summary>
        public AtomicResult Dispatch(string toolId, string jsonArguments)
        {
            Debug.WriteLine($"\n[DISPATCHER] >>> Processing: {toolId}");

            // 1. Поиск типа команды в реестре
            if (!_csCommands.TryGetValue(toolId, out var commandType) || commandType == null)
            {
                string errorMsg = $"Command '{toolId}' not found in registered commands.";
                Debug.WriteLine($"[DISPATCHER] !!! {errorMsg}");
                return new AtomicResult { Success = false, Message = errorMsg };
            }

            // 2. Десериализация параметров
            var parameters = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonArguments)
                             ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // 3. Создание инстанса C#-команды
            var instance = (IAtomicCommand)Activator.CreateInstance(commandType);

            // МАППИНГ ПОРТОВ: Чистый, изолированный проброс In -> Out -> Params
            MapProperties(instance, parameters, toolId);

            // 4. Выполнение логики команды (Транзакция контролируется снаружи в TransactionHandler)
            try
            {
                return instance.Execute(parameters); // Передаем параметры для совместимости, если нужно
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DISPATCHER] !!! Critical Error executing '{toolId}': {ex.Message}");
                throw; // Пробрасываем наверх для совершения Rollback
            }
        }


        /// <summary>
        /// Последовательно инжектирует данные в порты команды строго по алфавиту
        /// </summary>
        private void MapProperties(IAtomicCommand instance, Dictionary<string, object> parameters, string toolId)
        {
            var type = instance.GetType();

            // === ПОРТ 1. IN ===
            var inProp = type.GetProperty("In", BindingFlags.Public | BindingFlags.Instance);
            if (inProp != null && inProp.CanWrite)
            {
                if (parameters.TryGetValue("In", out var inTagObj) && inTagObj != null)
                {
                    inProp.SetValue(instance, inTagObj.ToString().Trim());
                    Debug.WriteLine($"[MAPPER] Port 'In' mapped: '{inTagObj}'");
                }
                else
                {
                    inProp.SetValue(instance, null);
                }
            }

            // === ПОРТ 2. OUT ===
            var outProp = type.GetProperty("Out", BindingFlags.Public | BindingFlags.Instance);
            if (outProp != null && outProp.CanWrite)
            {
                if (parameters.TryGetValue("Out", out var outTagObj) && outTagObj != null)
                {
                    outProp.SetValue(instance, outTagObj.ToString().Trim());
                    Debug.WriteLine($"[MAPPER] Port 'Out' mapped: '{outTagObj}'");
                }
                else
                {
                    outProp.SetValue(instance, null);
                }
            }

            // === ПОРТ 3. PARAMS ===
            var paramsProp = type.GetProperty("Params", BindingFlags.Public | BindingFlags.Instance);
            if (paramsProp != null && paramsProp.CanWrite)
            {
                try
                {
                    var attr = paramsProp.GetCustomAttribute<AiParamAttribute>();
                    Type schemaType = attr?.SchemaType ?? typeof(DynamicBimContract);
                    var paramsContract = (DynamicBimContract)Activator.CreateInstance(schemaType);

                    if (parameters.TryGetValue("Params", out var rawParamsObj) && rawParamsObj != null)
                    {
                        Dictionary<string, object> innerParams = null;

                        if (rawParamsObj is JObject jObj)
                            innerParams = jObj.ToObject<Dictionary<string, object>>();
                        else if (rawParamsObj is string jsonStr)
                            innerParams = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonStr);
                        else
                            innerParams = rawParamsObj as Dictionary<string, object>;

                        if (innerParams != null)
                        {
                            var schemaProps = schemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                            foreach (var kp in innerParams)
                            {
                                // 1. Всегда пишем в базовый словарь контракта
                                paramsContract.Set(kp.Key, kp.Value);

                                // 2. Умная запись в автосвойства схемы
                                var sProp = schemaProps.FirstOrDefault(p => p.Name.Equals(kp.Key, StringComparison.OrdinalIgnoreCase));
                                if (sProp != null && sProp.CanWrite && kp.Value != null)
                                {
                                    try
                                    {
                                        if (kp.Value is JToken token)
                                        {
                                            if (sProp.PropertyType == typeof(List<Dictionary<string, object>>))
                                            {
                                                var typedList = token.ToObject<List<Dictionary<string, object>>>();
                                                var caseInsensitiveList = typedList
                                                    .Select(d => new Dictionary<string, object>(d, StringComparer.OrdinalIgnoreCase))
                                                    .ToList();

                                                sProp.SetValue(paramsContract, caseInsensitiveList);
                                            }
                                            else
                                            {
                                                sProp.SetValue(paramsContract, token.ToObject(sProp.PropertyType));
                                            }
                                        }
                                        else
                                        {
                                            // Записываем простое значение, ТОЛЬКО если это не JToken
                                            sProp.SetValue(paramsContract, kp.Value);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"[MAPPER] Non-critical mapping fail for property '{kp.Key}': {ex.Message}");
                                    }
                                }
                            }
                        }
                    }

                    // ФИКС: Просто инжектируем paramsContract. .NET примет его без кастов, 
                    // так как у всех команд тип свойства Params — это DynamicBimContract!
                    paramsProp.SetValue(instance, paramsContract);
                    Debug.WriteLine($"[MAPPER] Port 'Params' successfully populated for {schemaType.Name}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MAPPER] !!! Critical Error in Params mapping: {ex.Message}");
                    throw;
                }
            }
        }
    }
}