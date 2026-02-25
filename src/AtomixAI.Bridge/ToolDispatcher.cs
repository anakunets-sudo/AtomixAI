using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using AtomixAI.Atomic;
using AtomixAI.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

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
            _csCommands = Assembly.GetAssembly(typeof(Registry))
                .GetTypes()
                .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToDictionary(
                    t => t.GetCustomAttribute<AtomicInfoAttribute>()?.Name ?? t.Name,
                    t => t,
                    StringComparer.OrdinalIgnoreCase
                );

            Debug.WriteLine($"[DISPATCHER] Initialized. Commands found: {_csCommands.Count}");
        }

        public AtomicResult Dispatch(string toolId, string jsonArguments)
        {
            Debug.WriteLine($"\n[DISPATCHER] >>> Processing: {toolId}");

            try
            {
                // 1. Десериализация входных параметров
                var parameters = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonArguments)
                                 ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                // 2. Поиск команды в реестре
                if (!_csCommands.TryGetValue(toolId, out var commandType))
                {
                    return new AtomicResult { Success = false, Message = $"Command {toolId} not found." };
                }

                // 3. Создание инстанса и маппинг In/Out/Params
                var instance = (IAtomicCommand)Activator.CreateInstance(commandType);
                MapProperties(instance, parameters);

                // 4. ВЫПОЛНЕНИЕ В ТРАНЗАКЦИИ REVIT
                // Здесь происходит вся магия: поиск, фильтрация, запись в AtomicStorage
                AtomicResult result = TransactionManager.ExecuteSafe(toolId, () =>
                {
                    return instance.Execute(parameters);
                });

                // 5. ОТПРАВКА ОБРАТНОЙ СВЯЗИ (FEEDBACK) ДЛЯ ORCHESTRATOR.PY
                if (result != null && _mcpHost != null)
                {
                    // Формируем спец-пакет, который Python интерпретирует как конец ожидания
                    var feedback = new
                    {
                        action = "tool_execution_result",
                        tool = toolId,
                        success = result.Success,
                        message = result.Message,
                        data = result.Data // Например, число найденных элементов (10)
                    };

                    // Сериализуем и кладем в очередь широковещания McpHost
                    string jsonFeedback = JsonConvert.SerializeObject(feedback);
                    _mcpHost.BroadcastToClients(jsonFeedback);

                    Debug.WriteLine($"[DISPATCHER] ✓ Feedback queued for Python: {result.Message}");
                }
                else if (_mcpHost == null)
                {
                    Debug.WriteLine("[DISPATCHER] !!! WARNING: McpHost not registered. Python will hang in wait loop.");
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DISPATCHER] !!! Critical Error: {ex.Message}");
                return new AtomicResult { Success = false, Message = $"Dispatch Error: {ex.Message}" };
            }
        }

        private void MapProperties(IAtomicCommand instance, Dictionary<string, object> parameters)
        {
            var props = instance.GetType().GetProperties();
            foreach (var prop in props)
            {
                // Добавь это условие: проверяем, есть ли у свойства SetMethod
                if (!prop.CanWrite || prop.GetSetMethod() == null) continue;

                // Ищем совпадение имени свойства команды с ключом в JSON
                if (parameters.TryGetValue(prop.Name, out var val) && val != null)
                {
                    try
                    {
                        object converted = null;

                        // 1. Обработка сложных объектов (фильтры JArray/JObject)
                        if (val is JToken token)
                        {
                            if (prop.PropertyType == typeof(JArray))
                            {
                                // Если пришел объект {}, превращаем его в массив [{}]
                                if (token is JObject obj)
                                {
                                    converted = new JArray(obj);
                                    Debug.WriteLine($"[MAPPER] Auto-wrapped JObject into JArray for {prop.Name}");
                                }
                                else
                                {
                                    converted = token as JArray ?? JArray.FromObject(token);
                                }
                            }
                            else if (prop.PropertyType == typeof(JObject))
                            {
                                converted = token is JObject jObj ? jObj : JObject.FromObject(token);
                            }
                            else
                            {
                                converted = token.ToObject(prop.PropertyType);
                            }
                        }
                        // 2. Конвертация единиц измерения Revit (Double/Feet)
                        else if (prop.PropertyType == typeof(double))
                        {
                            converted = Util.ParseToRevitFeet(val);
                        }
                        // 3. Прямой маппинг строк (In, Out) и базовых типов
                        else
                        {
                            // Просто конвертируем "walls_1" (string) в свойство In (string)
                            converted = Convert.ChangeType(val, prop.PropertyType);
                        }

                        prop.SetValue(instance, converted);
                        Debug.WriteLine($"[MAPPER] Property {prop.Name} set to: {converted}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MAPPER] !!! Error mapping '{prop.Name}': {ex.Message}");
                    }
                }
            }
        }
    }
}