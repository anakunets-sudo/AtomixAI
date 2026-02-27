using Newtonsoft.Json;
using System;

namespace AtomixAI.Core
{
    /// <summary>
    /// Универсальный контейнер результата выполнения команды для AtomixAI.
    /// Спроектирован для легкой передачи через MCP (JSON) и управления транзакциями Revit.
    /// </summary>
    public class AtomicResult
    {
        /// <summary>
        /// Главный флаг успеха. 
        /// Если IsSuccess == false, TransactionManager автоматически выполнит Rollback.
        /// </summary>
        [JsonProperty("success")]
        public bool Success { get; set; }

        /// <summary>
        /// Пояснительное сообщение для LLM (Claude/GPT) или пользователя.
        /// Описывает итог операции: "Created 10 walls" или "Selection failed".
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>
        /// Легкие данные для ИИ (обычно Count, ID или упрощенный JSON-объект).
        /// Если Data == null, ToolDispatcher интерпретирует это как команду-манипулятор (Link In->Out).
        /// </summary>
        [JsonProperty("data")]
        public object Data { get; set; }

        /// <summary>
        /// Внутренний тип данных для отладки в C#. Не сериализуется в JSON для ИИ.
        /// </summary>
        [JsonIgnore]
        public string DataType => Data?.GetType().Name ?? "null";

        // --- СТАТИЧЕСКИЕ МЕТОДЫ (ФАБРИКИ) ---

        /// <summary>
        /// Создает успешный результат.
        /// </summary>
        /// <param name="message">Описание успеха.</param>
        /// <param name="data">Данные для передачи (автоматически конвертируются в легкий вид базовым классом).</param>
        public static AtomicResult Ok(string message = "Success", object data = null)
        {
            return new AtomicResult
            {
                Success = true,
                Message = message,
                Data = data
            };
        }

        /// <summary>
        /// Создает результат с ошибкой. Триггерит откат транзакции Revit и AtomicStorage.
        /// </summary>
        /// <param name="message">Причина сбоя.</param>
        public static AtomicResult Error(string message)
        {
            return new AtomicResult
            {
                Success = false,
                Message = message,
                Data = null
            };
        }

        // --- ДОПОЛНИТЕЛЬНЫЕ МЕТОДЫ ---

        /// <summary>
        /// Позволяет "приклеить" данные к результату в цепочке вызовов (Fluent API).
        /// </summary>
        public AtomicResult WithData(object data)
        {
            this.Data = data;
            return this;
        }

        public override string ToString()
        {
            string status = Success ? "SUCCESS" : "ERROR";
            return $"[{status}] {Message} (Data: {DataType})";
        }
    }
}
