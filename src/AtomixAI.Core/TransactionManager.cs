using AtomixAI.Core;
using Autodesk.Revit.DB;
using System;
using System.Diagnostics;

namespace AtomixAI.Core
{
    public static class TransactionManager
    {
        public static Func<string, ITransactionHandler>? TransactionFactory { get; set; }

        [ThreadStatic]
        private static ITransactionHandler? _currentHandler;
        public static ITransactionHandler? CurrentHandler => _currentHandler;

        public static AtomicResult ExecuteSequence(string name, Func<List<AtomicResult>> sequenceAction)
        {
            var snapshot = AtomicStorage.GetCurrentContext();

            // Создаем локальную ссылку на хендлер
            using (var handler = TransactionFactory?.Invoke(name))
            {
                if (handler == null) return AtomicResult.Error("Factory not initialized");

                _currentHandler = handler; // Даем доступ командам внутри
                try
                {
                    var results = sequenceAction();

                    if (results.Any(r => !r.Success))
                    {
                        handler.Rollback(); // Вызываем у локальной переменной
                        RollbackStorage(snapshot);
                        return AtomicResult.Error("Sequence failed.");
                    }

                    handler.Assimilate();
                    return AtomicResult.Ok();
                }
                finally
                {
                    _currentHandler = null; // Чистим статику только в самом конце батча
                }
            }
        }

        public static AtomicResult ExecuteSafe(string name, Func<AtomicResult> action)
        {
            // Если мы уже внутри ExecuteSequence, просто выполняем действие
            if (_currentHandler != null)
            {
                return action();
            }

            // Если это одиночный вызов — создаем новый хендлер
            using (var handler = TransactionFactory?.Invoke(name))
            {
                _currentHandler = handler;
                try
                {
                    var result = action();
                    if (result != null && result.Success) handler.Assimilate();
                    else handler.Rollback();
                    return result;
                }
                finally { _currentHandler = null; }
            }
        }

        /// <summary>
        /// Удаляет из хранилища все ключи, которые появились в процессе выполнения неудачной команды.
        /// </summary>
        private static void RollbackStorage(string[] snapshot)
        {
            try
            {
                var currentKeys = AtomicStorage.GetCurrentContext();
                // Находим ключи, которых не было в снимке (новые "грязные" данные)
                var dirtyKeys = currentKeys.Except(snapshot).ToList();

                foreach (var key in dirtyKeys)
                {
                    AtomicStorage.Remove(key);
                    Debug.WriteLine($"[TransactionManager] Storage Rollback: Removed dirty key '{key}'");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TransactionManager] Critical error during Storage Rollback: {ex.Message}");
            }
        }

        // Перегрузка для простых Action (также с защитой данных)
        public static AtomicResult ExecuteSafe(string name, Action action)
        {
            return ExecuteSafe(name, () => {
                action();
                return new AtomicResult { Success = true };
            });
        }
    }
}