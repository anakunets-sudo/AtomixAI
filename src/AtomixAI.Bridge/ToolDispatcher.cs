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
                    /*return TransactionManager.ExecuteSafe(toolId, () => { 
                        instance.Execute(parameters); 
                    });*/

                    if (instance is IAtomicCommandInfo)
                    {
                        return instance.Execute(parameters);
                    }

                    // Иначе — упаковываем в транзакцию через ExternalEvent 
                    return TransactionManager.ExecuteSafe(toolId, () => instance.Execute(parameters));
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