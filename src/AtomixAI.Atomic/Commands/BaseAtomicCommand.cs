using AtomixAI.Core;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Atomic.Commands
{
    public abstract class BaseAtomicCommand : IAtomicCommand
    {
        [AtomicParam("Unique command name.")]
        public string CommandId => this.GetType().GetCustomAttribute<AtomicInfoAttribute>()?.Name;

        [AtomicParam("INPUT_PORT: Accepts a data tag. Defaults to '_last'.")]
        public string In { get; set; } = "_last";

        [AtomicParam("OUTPUT_PORT: Creates a new data tag.")]
        public string Out { get; set; }

        // --- ВХОД (ДАННЫЕ ИЗ ХРАНИЛИЩА) ---
        protected AtomicResult GetInput<T>(out T value)
        {
            value = default;
            string activeIn = string.IsNullOrWhiteSpace(In) || In.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? "_last" : In;

            var rawData = AtomicStorage.Get(activeIn);

            if (rawData == null)
                return AtomicResult.Error($"Chain broken: Tag '{activeIn}' is empty.");

            // 1. Прямое совпадение типов
            if (rawData is T typedData)
            {
                value = typedData;
                return AtomicResult.Ok();
            }

            // 2. АВТО-УПАКОВКА: Если в хранилище объект (ElementId), а просят список (List<ElementId>)
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(typeof(T)) && typeof(T) != typeof(string))
            {
                Type elementType = typeof(T).IsGenericType
                    ? typeof(T).GetGenericArguments()[0]
                    : typeof(object);

                if (elementType.IsAssignableFrom(rawData.GetType()))
                {
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var newList = (System.Collections.IList)Activator.CreateInstance(listType);
                    newList.Add(rawData);
                    value = (T)newList;

                    Debug.WriteLine($"[BASE-CMD] 🎁 Auto-wrapped {rawData.GetType().Name} into {typeof(T).Name} for tag '{activeIn}'");
                    return AtomicResult.Ok();
                }
            }

            // 3. АВТО-РАСПАКОВКА (опционально): Если в хранилище список из 1 элемента, а просят один объект
            if (rawData is System.Collections.IList list && list.Count == 1)
            {
                var firstItem = list[0];
                if (firstItem is T singleTypedData)
                {
                    value = singleTypedData;
                    return AtomicResult.Ok();
                }
            }

            return AtomicResult.Error($"Type mismatch: Tag '{activeIn}' is {rawData.GetType().Name}, expected {typeof(T).Name}.");
        }

        // --- ВЫХОД (СОХРАНЕНИЕ И ОТЧЕТ ДЛЯ ИИ) ---

        // Вариант 1: Автоматический
        protected AtomicResult SetOutput(object storageValue, bool success, string message = null)
        {
            return SetOutput<object>(storageValue, null, success, message);
        }

        // Вариант 2: Явный (с dataOverride для ИИ)
        protected AtomicResult SetOutput<TData>(object storageValue, TData dataOverride, bool success, string message = null)
        {
            // 1. ПОДГОТОВКА ДАННЫХ ДЛЯ ИИ (Result.Data)
            // ИИ должен видеть "4", а не список ID.
            object finalDataForAi = dataOverride != null ? (object)dataOverride : ExtractData(storageValue);

            // 2. РАБОТА С ХРАНИЛИЩЕМ (AtomicStorage)
            if (!string.IsNullOrWhiteSpace(Out) && !Out.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                if (!success)
                {
                    AtomicStorage.Remove(Out); // Ошибка — сжигаем тег
                }
                else if (storageValue == null)
                {
                    // РЕЖИМ LINK: Если команда (Select) не меняла данные, просто создаем ссылку
                    AtomicStorage.Link(In, Out);
                    // Для ИИ берем данные из первоисточника (In)
                    finalDataForAi = ExtractData(AtomicStorage.Get(In));
                }
                else
                {
                    // РЕЖИМ SET: В хранилище ВСЕГДА кладем ОРИГИНАЛ (storageValue), а не число!
                    AtomicStorage.Set(Out, storageValue);
                }
            }

            // Возвращаем результат, где Data — это число для ИИ, а в Storage уже лежит список
            return new AtomicResult
            {
                Success = success,
                Data = finalDataForAi,
                Message = message ?? (success ? "Success" : "Operation failed")
            };
        }

        private object ExtractData(object value)
        {
            if (value == null) return 0;
            if (value is System.Collections.ICollection col) return col.Count;
            if (value is ElementId id)
            {
#if REVIT2024_OR_GREATER
                return id.Value.ToString();
#else
                return id.IntegerValue;
#endif
            }
            return value;
        }

        public AtomicResult Execute(Dictionary<string, object> parameters)
        {
            try
            {
                var handler = TransactionManager.CurrentHandler;
                if (handler?.UIDoc?.Document == null)
                    return AtomicResult.Error("No active Revit document found.");

                return Execute(handler);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{this.GetType().Name}] Failed: {ex.Message}");
                return AtomicResult.Error(ex.Message);
            }
        }

        protected abstract AtomicResult Execute(ITransactionHandler handler);
    }
}
