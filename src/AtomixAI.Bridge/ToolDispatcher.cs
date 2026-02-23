using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using AtomixAI.Atomic;
using AtomixAI.Core;
using Newtonsoft.Json;
using Autodesk.Revit.DB;

namespace AtomixAI.Bridge
{
    public class ToolDispatcher
    {
        private readonly Dictionary<string, Type> _csCommands;
        private readonly PyRevitLoader _pyLoader;

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

            Debug.WriteLine($"[DISPATCHER] Инициализирован. Найдено команд: {_csCommands.Count}");
        }

        public AtomicResult Dispatch(string toolId, string jsonArguments)
        {
            Debug.WriteLine($"\n[DISPATCHER] >>> Входящий вызов: {toolId}");

            try
            {
                var parameters = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonArguments)
                                 ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                if (_csCommands.TryGetValue(toolId, out var commandType))
                {
                    var instance = (IAtomicCommand)Activator.CreateInstance(commandType);

                    // 1. МАППИНГ И РЕЗОЛВ (Wall_1 -> ElementId)
                    MapProperties(instance, parameters);

                    // 2. ВЫПОЛНЕНИЕ С ВОЗВРАТОМ РЕЗУЛЬТАТА ИЗ ЛЯМБДЫ
                    // Мы вызываем перегрузку ExecuteSafe, которая возвращает AtomicResult
                    AtomicResult result = TransactionManager.ExecuteSafe(toolId, () =>
                    {
                        var cmdResult = instance.Execute(parameters);
                        Debug.WriteLine($"[DISPATCHER] Команда выполнена внутри транзакции. Data: {cmdResult.Data ?? "NULL"}");
                        return cmdResult;
                    });

                    // 3. ПРОВЕРКА ПОЛУЧЕННЫХ ДАННЫХ
                    if (result != null && result.Success)
                    {
                        Debug.WriteLine($"[DISPATCHER] Успех. Получены данные для сохранения: {result.Data ?? "NULL"}");

                        if (result.Data != null)
                        {
                            ProcessResultAlias(instance, result.Data);
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[DISPATCHER] ✗ Ошибка или пустой результат: {result?.Message ?? "No Result"}");
                    }

                    return result;
                }

                return new AtomicResult { Success = false, Message = $"Command {toolId} not found." };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DISPATCHER] !!! Критическая ошибка: {ex.Message}");
                return new AtomicResult { Success = false, Message = ex.Message };
            }
        }


        private void MapProperties(IAtomicCommand instance, Dictionary<string, object> parameters)
        {
            var props = instance.GetType().GetProperties();
            foreach (var prop in props)
            {
                if (parameters.TryGetValue(prop.Name, out var val) && val != null)
                {
                    try
                    {
                        object converted = null;
                        string rawVal = val.ToString();

                        // --- AUTO-RESOLVE LOGIC ---
                        // Проверяем: это строка? Не число с юнитом (6000mm)? Есть в Сторадже?
                        if (val is string alias && !char.IsDigit(alias[0]) && AtomicStorage.Has(alias))
                        {
                            var storedValue = AtomicStorage.Get(alias);
                            if (storedValue != null)
                            {
                                // Если свойство ждет ElementId, а в сторадже лежит подходящий тип
                                if (prop.PropertyType == typeof(ElementId) || prop.PropertyType == typeof(long))
                                {
                                    converted = storedValue;
                                    Debug.WriteLine($"[RESOLVER] ✓ Алиас '{alias}' заменен на ID: {storedValue} для {prop.Name}");
                                }
                            }
                        }

                        // --- СТАНДАРТНЫЙ МАППИНГ ---
                        if (converted == null)
                        {
                            if (prop.PropertyType == typeof(double))
                                converted = Util.ParseToRevitFeet(val);
                            else if (prop.PropertyType == typeof(ElementId))
#if REVIT2025_OR_GREATER
                                converted = new ElementId(Convert.ToInt64(val));
#else
                                converted = new ElementId(Convert.ToInt32(val));
#endif
                            else
                                converted = Convert.ChangeType(val, prop.PropertyType);
                        }

                        prop.SetValue(instance, converted);
                        Debug.WriteLine($"[DISPATCHER] Property {prop.Name} = {converted}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DISPATCHER] ! Ошибка маппинга {prop.Name}: {ex.Message}");
                    }
                }
            }
        }

        private void ProcessResultAlias(IAtomicCommand instance, object data)
        {
            // Ищем свойство ResultAlias (согласно интерфейсу)
            var aliasProp = instance.GetType().GetProperty("ResultAlias");
            var aliasValue = aliasProp?.GetValue(instance)?.ToString();

            if (!string.IsNullOrEmpty(aliasValue))
            {
                Debug.WriteLine($"[DISPATCHER] Сохранение в контекст: '{aliasValue}' -> {data}");
                AtomicStorage.Set(aliasValue, data);
                Debug.WriteLine($"[DISPATCHER] ✓ AtomicStorage обновлен.");
            }
            else
            {
                Debug.WriteLine("[DISPATCHER] ! ResultAlias пуст. Сохранение пропущено.");
            }
        }
    }
}
