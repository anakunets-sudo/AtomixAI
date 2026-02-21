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

        // Добавьте этот метод в класс ToolDispatcher
        public async Task<AtomicResult> DispatchAsync(string toolId, string jsonArguments)
        {
            // 1. Сначала подготавливаем команду (логика та же, что в обычном Dispatch)
            var rawParams = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonArguments) ?? new Dictionary<string, object>();
            var parameters = new Dictionary<string, object>(rawParams, StringComparer.OrdinalIgnoreCase);

            if (!_csCommands.TryGetValue(toolId, out var commandType))
            {
                return new AtomicResult { Success = false, Message = $"Command {toolId} not found." };
            }

            var instance = (IAtomicCommand)Activator.CreateInstance(commandType);

            // Заполнение свойств через Reflection (Length, Categories и т.д.)
            foreach (var prop in commandType.GetProperties())
            {
                if (parameters.TryGetValue(prop.Name, out var value) && value != null)
                {
                    try
                    {
                        object convertedValue = prop.PropertyType == typeof(double)
                            ? ParseToRevitFeet(value)
                            : Convert.ChangeType(value, prop.PropertyType);
                        prop.SetValue(instance, convertedValue);
                    }
                    catch { continue; }
                }
            }

            // 2. ВАЖНО: Вместо мгновенного ExecuteSafe, вызываем асинхронное выполнение
            // Это «заморозит» выполнение текущего потока до тех пор, пока Revit не отработает
            return await TransactionManager.ExecuteAsync(toolId, () => instance.Execute(parameters));
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

        public static double ParseToRevitFeet(object rawValue)
        {
            // Очистка строки и нормализация разделителей
            string input = rawValue?.ToString()?.ToLower()
                ?.Replace(" ", "").Replace(",", ".") ?? "0";

            // Выделяем только числовую часть
            string numericPart = new string(input.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            if (!double.TryParse(numericPart, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double value)) return 0;

            // Маппинг единиц измерения во внутренние футы Revit
            if (input.EndsWith("mm")) return value / 304.8;
            if (input.EndsWith("cm")) return value / 30.48;
            if (input.EndsWith("m")) return value / 0.3048;
            if (input.EndsWith("in")) return value / 12.0;
            if (input.EndsWith("ft")) return value; // Уже футы

            // По умолчанию (если единиц нет) считаем, что прилетели ММ
            return value / 304.8;
        }
    }
}