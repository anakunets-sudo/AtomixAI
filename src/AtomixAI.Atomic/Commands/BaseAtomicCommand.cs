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
        public string CommandId => this.GetType().GetCustomAttribute<AtomixAI.Core.AtomicInfoAttribute>()?.Name;

        [AtomicParam("INPUT_PORT: Accepts a data alias from a previous step's OUTPUT_PORT. " +
             "If you need to act on elements, this parameter MUST be connected to an existing alias.")]
        public string In { get; set; }

        [AtomicParam("OUTPUT_PORT: Creates a new data alias containing the results of this tool. " +
             "This alias can be used by any subsequent tool's INPUT_PORT.")]
        public string Out { get; set; }
        private static AtomicResult Ok() => new AtomicResult { Success = true };

        // --- ВХОД (ДАННЫЕ ИЗ СКЛАДА ДЛЯ C#) ---
        protected AtomicResult GetInput<T>(out T value)
        {
            value = default;

            // 1. Если In пуст или "none" — это режим "с нуля"
            if (string.IsNullOrWhiteSpace(In) || In.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return Ok();
            }

            // 2. Пытаемся извлечь данные из хранилища
            var rawData = AtomicStorage.Get(In);

            // 3. Проверка на "Разрыв цепи" (если алиас пуст или сгорел)
            if (rawData == null)
            {
                return new AtomicResult
                {
                    Success = false,
                    Message = $"Chain broken: Alias '{In}' is empty or does not exist. Previous step failed?"
                };
            }

            // 4. Проверка типа (Safe Cast)
            if (rawData is T typedData)
            {
                value = typedData;
                return Ok();
            }

            // 5. Ошибка несоответствия типов
            return new AtomicResult
            {
                Success = false,
                Message = $"Type mismatch: Alias '{In}' contains {rawData.GetType().Name}, but {typeof(T).Name} is required."
            };
        }

        // --- ВЫХОД (СОХРАНЕНИЕ И ОТЧЕТ ДЛЯ ИИ) ---

        // Вариант 1: Автоматический (для списков, ID и т.д.)
        /// <summary>
        /// 
        /// </summary>
        /// <param name="storageValue"></param>
        /// <param name="success">false если ошибка если оборавать код, иначе он продолжится в цепочке</param>
        /// <param name="message"></param>
        /// <returns></returns>
        protected AtomicResult SetOutput(object storageValue, bool success, string message = null)
        {
            return SetOutput<object>(storageValue, null, success, message);
        }

        // Вариант 2: Явный (когда нужно показать ИИ кастомный объект/анонимный класс)
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TData"></typeparam>
        /// <param name="storageValue"></param>
        /// <param name="dataOverride"></param>
        /// <param name="success">false если ошибка если оборавать код, иначе он продолжится в цепочке</param>
        /// <param name="message"></param>
        /// <returns></returns>
        protected AtomicResult SetOutput<TData>(object storageValue, TData dataOverride, bool success, string message = null)
        {
            // 1. Определяем, что увидит ИИ
            object? finalDataForAi = dataOverride != null ? (object)dataOverride : ExtractData(storageValue);

            // 2. Работа с хранилищем
            if (!string.IsNullOrWhiteSpace(Out) && !Out.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                bool isEmpty = storageValue == null || (storageValue is System.Collections.ICollection col && col.Count == 0);

                if (isEmpty || !success) // Если успех false - тоже сжигаем алиас, данные "отравлены"
                {
                    AtomicStorage.Set(Out, null);
                    finalDataForAi = 0;
                }
                else
                {
                    AtomicStorage.Set(Out, storageValue);
                }
            }

            return new AtomicResult
            {
                Success = success, // Вот здесь твой контроль!
                Data = finalDataForAi,
                Message = message ?? (success ? "Success" : "Operation failed")
            };
        }

        // Вспомогательный метод: превращает тяжелые объекты Revit в легкие данные для ИИ
        private object ExtractData(object value)
        {
            if (value == null) return 0;
            if (value is System.Collections.ICollection col) return col.Count;
#if REVIT2024_OR_GREATER
            if (value is ElementId id) return id.Value;
#else
            if (value is ElementId id) return id.IntegerValue; // Для новых версий Revit: id.Value.ToString()
#endif
            return value;
        }

        // --- ИСПОЛНЕНИЕ (ENTRY POINT) ---
        public AtomicResult Execute(Dictionary<string, object> parameters)
        {
            try
            {
                var handler = TransactionManager.CurrentHandler;
                if (handler?.UIDoc?.Document == null)
                    return new AtomicResult { Success = false, Message = "No active Revit document found." };

                return Execute(handler);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{this.GetType().Name}] Failed: {ex.Message}");
                return new AtomicResult { Success = false, Message = ex.Message };
            }
        }

        // Абстрактный метод, который будут переопределять все твои 100+ команд
        protected abstract AtomicResult Execute(ITransactionHandler handler);
    }
}
