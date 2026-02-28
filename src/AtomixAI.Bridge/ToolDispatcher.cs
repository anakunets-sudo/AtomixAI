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

namespace AtomixAI.Bridge
{
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
                    t => t.GetCustomAttribute<AtomicInfoAttribute>()?.Name ?? t.Name,
                    t => t,
                    StringComparer.OrdinalIgnoreCase
                );

            Debug.WriteLine($"[DISPATCHER] Initialized. Commands found: {_csCommands.Count}");
        }

        public AtomicResult DispatchSequence(string jsonSequence)
        {
            Debug.WriteLine("[DISPATCHER] >>> Processing Sequence Batch");

            try
            {
                // Парсим массив команд из JSON
                var steps = JsonConvert.DeserializeObject<List<SequenceStep>>(jsonSequence);
                if (steps == null || steps.Count == 0)
                    return AtomicResult.Error("Empty sequence received.");

                // Запускаем через наш новый метод в TransactionManager
                return TransactionManager.ExecuteSequence("AtomixAI AI-Plan", () =>
                {
                    var results = new List<AtomicResult>();

                    foreach (var step in steps)
                    {
                        // Для каждого шага вызываем существующий метод Dispatch
                        // Но нам нужно передать аргументы как строку JSON, как того ожидает Dispatch
                        string argsJson = JsonConvert.SerializeObject(step.Arguments);

                        var stepResult = Dispatch(step.Tool, argsJson);
                        results.Add(stepResult);

                        // Если шаг не удался — прерываем выполнение цепочки немедленно
                        if (!stepResult.Success) break;
                    }

                    return results;
                });
            }
            catch (Exception ex)
            {
                return AtomicResult.Error($"Sequence Dispatch Error: {ex.Message}");
            }
        }

        // Вспомогательный класс для десериализации (можно положить в конец файла)
        public class SequenceStep
        {
            [JsonProperty("name")]
            public string Tool { get; set; }

            [JsonProperty("arguments")]
            public Dictionary<string, object> Arguments { get; set; }
        }
        
        public AtomicResult Dispatch(string toolId, string jsonArguments)
        {
            Debug.WriteLine($"\n[DISPATCHER] >>> Processing: {toolId}");

            try
            {
                // 1. Поиск типа команды в реестре
                // ИСПРАВЛЕНО: Добавлена проверка на наличие команды ПЕРЕД созданием инстанса
                if (!_csCommands.TryGetValue(toolId, out var commandType) || commandType == null)
                {
                    string errorMsg = $"Command '{toolId}' not found in registered commands.";
                    Debug.WriteLine($"[DISPATCHER] !!! {errorMsg}");
                    return new AtomicResult { Success = false, Message = errorMsg };
                }

                // 2. Десериализация параметров
                var parameters = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonArguments)
                                 ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                // Логика 'In' по умолчанию
                if (!parameters.ContainsKey("In") || string.IsNullOrEmpty(parameters["In"]?.ToString()))
                {
                    parameters["In"] = "_last";
                    Debug.WriteLine("[DISPATCHER] ℹ 'In' was empty, auto-assigned to '_last'");
                }

                // 3. Создание инстанса команды (теперь безопасно)
                var instance = (IAtomicCommand)Activator.CreateInstance(commandType);

                // Маппинг свойств из JSON в объект команды
                MapProperties(instance, parameters);

                // 4. ВЫПОЛНЕНИЕ в безопасном контексте транзакций Revit
                AtomicResult result = TransactionManager.ExecuteSafe(toolId, () => {
                    return instance.Execute(parameters);
                });
                /*
                // 5. Обработка результатов и хранилища (Storage)
                if (result != null && result.Success)
                {
                    string outKey = parameters.ContainsKey("Out") ? parameters["Out"].ToString() : null;

                    // Если команда не реализует BaseAtomicCommand (где логика SetOutput встроена)
                    // Но мы все равно хотим сохранить результат
                    if (result.Data != null && !string.IsNullOrEmpty(outKey))
                    {
                        AtomicStorage.Set(outKey, result.Data);
                        Debug.WriteLine($"[DISPATCHER] 💾 New data saved to storage: {outKey}");
                    }
                }*/

                // 6. Отправка обратной связи в Python через McpHost
                if (_mcpHost != null && result != null)
                {
                    var feedback = new
                    {
                        action = "tool_execution_result",
                        tool = toolId,
                        success = result.Success,
                        message = result.Message,
                        data = result.Data
                    };

                    _mcpHost.BroadcastToClients(JsonConvert.SerializeObject(feedback));
                    Debug.WriteLine($"[DISPATCHER] ✓ Feedback sent to Python: {result.Message}");
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DISPATCHER] !!! Critical Error in '{toolId}': {ex.Message}");
                return new AtomicResult { Success = false, Message = $"Dispatch Error: {ex.Message}" };
            }
        }

        private void MapProperties(IAtomicCommand instance, Dictionary<string, object> parameters)
        {
            var props = instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                // Пропускаем свойства без сеттера или отсутствующие в запросе
                if (!prop.CanWrite || prop.GetSetMethod() == null) continue;
                if (!parameters.TryGetValue(prop.Name, out var rawValue) || rawValue == null) continue;

                try
                {
                    object finalValue = rawValue;

                    // 1. КОНВЕРТАЦИЯ ТИПОВ (JToken -> C# Type)
                    if (finalValue is JToken jToken)
                    {
                        finalValue = jToken.ToObject(prop.PropertyType);
                    }

                    // 2. ОБРАБОТКА ЕДИНИЦ (Только для double)
                    if (prop.PropertyType == typeof(double))
                    {
                        // Используем ваш Util для перевода "500mm" -> 1.6404 (feet)
                        finalValue = Util.ParseToRevitFeet(finalValue);
                    }

                    // 3. ПРИВЕДЕНИЕ ТИПОВ (Для простых типов)
                    else if (!prop.PropertyType.IsAssignableFrom(finalValue.GetType()))
                    {
                        finalValue = Convert.ChangeType(finalValue, prop.PropertyType);
                    }

                    prop.SetValue(instance, finalValue);
                    // Debug.WriteLine($"[MAPPER] Property {prop.Name} set to: {finalValue}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MAPPER] !!! Error mapping '{prop.Name}': {ex.Message}");
                }
            }
        }
    }
}