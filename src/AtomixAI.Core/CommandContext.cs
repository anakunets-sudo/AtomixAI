using System;
using System.Collections.Generic;
using System.Linq;

namespace AtomixAI.Core
{
    /// <summary>
    /// Хранит историю выполненных команд и их результатов
    /// Ограничена последними N командами для экономии памяти
    /// </summary>
    public static class CommandContext
    {
        private static readonly List<CommandRecord> _history = new List<CommandRecord>();
        private static readonly object _lockObj = new object();
        public static int MaxHistory { get; set; } = 20;

        /// <summary>
        /// Запись о выполненной команде
        /// </summary>
        public class CommandRecord
        {
            public string CommandId { get; set; }
            public string CommandName { get; set; }
            public Dictionary<string, object> Parameters { get; set; }
            public string ResultAlias { get; set; }
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public DateTime ExecutedAt { get; set; }
            public long ExecutionTimeMs { get; set; }
        }

        /// <summary>
        /// Минимальное резюме для отправки ИИ (экономит токены)
        /// </summary>
        public class CommandSummary
        {
            public int Index { get; set; }
            public string CommandName { get; set; }
            public Dictionary<string, object> ParamsSummary { get; set; }
            public string ResultAlias { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
        }

        /// <summary>
        /// Добавить новую команду в историю
        /// </summary>
        public static void AddCommand(string commandName, Dictionary<string, object> parameters,
            string resultAlias, bool success, string errorMessage = null, long executionTimeMs = 0)
        {
            lock (_lockObj)
            {
                // Если истории больше MaxHistory - удаляем старейшую
                if (_history.Count >= MaxHistory)
                {
                    var oldestAlias = _history[0].ResultAlias;
                    _history.RemoveAt(0);

                    // Удаляем старые данные из AtomicStorage
                    if (!string.IsNullOrEmpty(oldestAlias))
                    {
                        AtomicStorage.Remove(oldestAlias);
                    }
                }

                var record = new CommandRecord
                {
                    CommandId = GenerateCommandId(),
                    CommandName = commandName,
                    Parameters = parameters ?? new Dictionary<string, object>(),
                    ResultAlias = resultAlias,
                    Success = success,
                    ErrorMessage = errorMessage,
                    ExecutedAt = DateTime.UtcNow,
                    ExecutionTimeMs = executionTimeMs
                };

                _history.Add(record);

                System.Diagnostics.Debug.WriteLine(
                    $"[CommandContext] Added: {commandName} -> {resultAlias} (Success: {success})");
            }
        }

        /// <summary>
        /// Получить краткое резюме всех команд для ИИ (экономит токены)
        /// Отправляется ИИ вместо полных данных
        /// </summary>
        public static List<CommandSummary> GetContextSummary()
        {
            lock (_lockObj)
            {
                return _history.Select((cmd, index) => new CommandSummary
                {
                    Index = index,
                    CommandName = cmd.CommandName,
                    ParamsSummary = SummarizeParams(cmd.Parameters),
                    ResultAlias = cmd.ResultAlias,
                    Success = cmd.Success,
                    Error = cmd.ErrorMessage
                }).ToList();
            }
        }

        /// <summary>
        /// Получить полную запись команды по индексу или имени
        /// </summary>
        public static CommandRecord GetCommand(int index)
        {
            lock (_lockObj)
            {
                return index >= 0 && index < _history.Count ? _history[index] : null;
            }
        }

        public static CommandRecord GetCommandByAlias(string alias)
        {
            lock (_lockObj)
            {
                return _history.FirstOrDefault(c => c.ResultAlias == alias);
            }
        }

        /// <summary>
        /// Очистить историю (при перезагрузке сессии)
        /// </summary>
        public static void Clear()
        {
            lock (_lockObj)
            {
                _history.Clear();
                System.Diagnostics.Debug.WriteLine("[CommandContext] History cleared.");
            }
        }

        /// <summary>
        /// Получить количество команд в истории
        /// </summary>
        public static int Count
        {
            get
            {
                lock (_lockObj)
                {
                    return _history.Count;
                }
            }
        }

        // ============== ВНУТРЕННИЕ МЕТОДЫ ==============

        /// <summary>
        /// Создать краткое резюме параметров (без полных значений)
        /// </summary>
        private static Dictionary<string, object> SummarizeParams(Dictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return new Dictionary<string, object>();

            var summary = new Dictionary<string, object>();

            foreach (var param in parameters)
            {
                // Пропускаем ResultAlias - он не нужен в резюме
                if (param.Key.Equals("ResultAlias", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Для больших объектов - только тип и размер
                if (param.Value is System.Collections.ICollection collection)
                {
                    summary[param.Key] = $"<{collection.Count} items>";
                }
                else if (param.Value is string str && str.Length > 100)
                {
                    summary[param.Key] = $"<string:{str.Length} chars>";
                }
                else
                {
                    // Для скалярных значений - сохраняем как есть
                    summary[param.Key] = param.Value;
                }
            }

            return summary;
        }

        /// <summary>
        /// Сгенерировать уникальный ID команды
        /// </summary>
        private static string GenerateCommandId()
        {
            return $"cmd_{DateTime.UtcNow.Ticks}";
        }
    }
}