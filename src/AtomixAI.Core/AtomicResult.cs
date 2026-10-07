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
        /// Запрос прерван пользователем. Оркестратор не должен превращать это в ответ чата.
        /// </summary>
        [JsonProperty("cancelled")]
        public bool Cancelled { get; set; }

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


        public static readonly string DefaultPrompt =
            "### OUTPUT RULES:\n" + "1. BREVITY: Past tense, one short sentence only. No intros.\n" + "2. NO LISTS: Do not use bullet points or numbered lists.\n" + "3. PLAIN TEXT ONLY: Write one natural sentence.\n";

        public static readonly string SuccessPrompt = "4. VARIABLE TAGS: Include ONLY unique tags from message to FINAL result.\n" + "5. NO GHOST TAGS: Never mention a tag not specified in the message.\n" + "6. SYNC: Use specified tags exactly.\n" + "6. MANDATORY HASHTAG PREFIX: You MUST always keep the '#' symbol prefix before any tag name (e.g., '#new_wall_1', '#found_walls_2'). Removing or omitting the '#' symbol is a CRITICAL ERROR.\n";

        public static readonly string ErrorPrompt = "4. DO NOT include '#' symbols in your answer. Describe the error briefly.\n";

        [JsonProperty("prompt")]
        public string Prompt { get; private set; } = string.Empty;

        [JsonIgnore]
        public PromptBehavior PromptBehavior { get; private set; } = PromptBehavior.Default;

        // --- СТАТИЧЕСКИЕ МЕТОДЫ (ФАБРИКИ) ---

        public void PromptOverride(string prompt)
        {
            Prompt = prompt ?? string.Empty; 
            PromptBehavior = PromptBehavior.Override;
        }

        public void PromptAppend(string prompt)
        {
            Prompt = prompt ?? string.Empty; 
            PromptBehavior = PromptBehavior.Append;
        }

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
                Data = null,
            };
        }

        /// <summary>
        /// Отмена пользователем. Success = false, чтобы TransactionManager откатил группу.
        /// </summary>
        public static AtomicResult Cancel(string message = "Cancelled by user.")
        {
            return new AtomicResult
            {
                Success = false,
                Cancelled = true,
                Message = message,
                Data = null,
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
