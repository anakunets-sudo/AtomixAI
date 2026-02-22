using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AtomixAI.Atomic;
using AtomixAI.Core;
using Newtonsoft.Json;

namespace AtomixAI.Bridge
{
    public class ToolDispatcher
    {
        private readonly Dictionary<string, Type> _csCommands;
        private readonly PyRevitLoader _pyLoader;

        public ToolDispatcher(string scriptsPath)
        {
            // 1. Инициализируем загрузчик Python-скриптов 
            _pyLoader = new PyRevitLoader(scriptsPath);

            // 2. Сканируем C# команды из сборки Atomic 
            _csCommands = Assembly.GetAssembly(typeof(Registry))
                .GetTypes()
                .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToDictionary(
                    t => t.GetCustomAttribute<AtomicInfoAttribute>()?.Name ?? t.Name,
                    t => t
                );
        }

        public AtomicResult Dispatch(string toolId, string jsonArguments)
        {
            try
            {
                // 1. Парсим JSON в словарь (нечувствительный к регистру для удобства поиска) 
                var rawParams = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonArguments) ?? new Dictionary<string, object>();

                // Создаем Case-Insensitive копию для маппинга 
                var parameters = new Dictionary<string, object>(rawParams, StringComparer.OrdinalIgnoreCase);

                if (_csCommands.TryGetValue(toolId, out var commandType))
                {
                    var instance = (IAtomicCommand)Activator.CreateInstance(commandType);
                    var properties = commandType.GetProperties();

                    // --- PHASE 1: RESOLVE ALIASES (Single & Lists) ---
                    var resolvedParams = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                    foreach (var kvp in parameters)
                    {
                        object val = kvp.Value;

                        // Сценарий А: Одиночный алиас (например, "Wall1")
                        if (val is string strVal)
                        {
                            var stored = AtomicStorage.Get(strVal);
                            resolvedParams[kvp.Key] = stored ?? val; // Если в памяти нет, оставляем строку
                        }
                        // Сценарий Б: Список алиасов (например, ["W1", "W2"])
                        else if (val is Newtonsoft.Json.Linq.JArray jArray)
                        {
                            // Пытаемся резолвить каждый элемент массива
                            var list = jArray.Select(item => {
                                string s = item.ToString();
                                return AtomicStorage.Get(s) ?? s;
                            }).ToList();

                            resolvedParams[kvp.Key] = list;
                        }
                        else
                        {
                            resolvedParams[kvp.Key] = val;
                        }
                    }

                    // --- PHASE 2: SET PROPERTIES ---
                    foreach (var prop in properties)
                    {
                        if (resolvedParams.TryGetValue(prop.Name, out var value) && value != null)
                        {
                            try
                            {
                                // Если свойство команды ждет ElementId, а мы нашли его в Storage
                                if (prop.PropertyType.IsAssignableFrom(value.GetType()))
                                {
                                    prop.SetValue(instance, value);
                                }
                                // Твоя существующая логика для double/RevitFeet
                                else if (prop.PropertyType == typeof(double))
                                {
                                    prop.SetValue(instance, Util.ParseToRevitFeet(value));
                                }
                                else
                                {
                                    prop.SetValue(instance, Convert.ChangeType(value, prop.PropertyType));
                                }
                            }
                            catch { continue; }
                        }
                    }

                    foreach (var prop in properties)
                    {
                        // Ищем значение в JSON по имени свойства (игнорируя регистр) 
                        if (parameters.TryGetValue(prop.Name, out var value) && value != null)
                        {
                            try
                            {
                                object convertedValue;
                                if (prop.PropertyType == typeof(double))
                                {
                                    // Конвертируем любую строку ("500mm", "10ft") в футы Revit 
                                    convertedValue = Util.ParseToRevitFeet(value);
                                }
                                else
                                {
                                    // Для остальных типов (string, int, bool) 
                                    convertedValue = Convert.ChangeType(value, prop.PropertyType);
                                }
                                prop.SetValue(instance, convertedValue);
                            }
                            catch
                            {
                                continue;
                            } // Пропускаем битые параметры 
                        }
                    }

                    // 2. Выполняем команду в безопасном контексте транзакции 
                    AtomicResult result;

                    if (instance is IAtomicCommandInfo)
                    {
                        result = instance.Execute(parameters);
                    }
                    else
                    {
                        result = TransactionManager.ExecuteSafe(toolId, () => instance.Execute(parameters));
                    }

                    result.Message += $"\n[DEBUG: Dispatcher] Checking for Alias. Params count: {parameters.Count}";

                    // --- АВТОМАТИЧЕСКОЕ СОХРАНЕНИЕ В STORAGE ---
                    // Проверяем, просил ли ИИ запомнить результат под алиасом
                    if (result.Success && parameters.TryGetValue("ResultAlias", out var aliasObj))
                    {
                        string alias = aliasObj?.ToString();
                        if (!string.IsNullOrEmpty(alias))
                        {
                            // 1. Сохраняем данные
                            AtomicStorage.Set(alias, result.Data);

                            // 2. Добавляем дебаг-метку в сообщение для проверки
                            string dataType = result.Data?.GetType().Name ?? "null";
                            result.Message += $"\n[DEBUG: Memory] Saved object '{alias}' of type '{dataType}' to Storage.";

                            // 3. (Опционально) Проверочное чтение
                            var checkValue = AtomicStorage.Get(alias);
                            if (checkValue != null)
                            {
                                result.Message += " [Verification: SUCCESS]";
                            }
                        }

                        else if (!result.Success)
                        {
                            result.Message += "\n[DEBUG: Memory] Skipped because result.Success is FALSE.";
                        }
                        else
                        {
                            result.Message += "\n[DEBUG: Memory] Skipped because ResultAlias was NOT found in parameters.";
                        }
                    }

                    return result;
                }

                return new AtomicResult { Success = false, Message = $"Command {toolId} not found." };
            }
            catch (Exception ex)
            {
                return new AtomicResult { Success = false, Message = $"Dispatcher Error: {ex.Message}" };
            }
        }
    }
}