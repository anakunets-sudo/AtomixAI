using AtomixAI.Core;
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

        public static AtomicResult ExecuteSafe(string name, Func<AtomicResult> action)
        {
            if (TransactionFactory == null) return new AtomicResult { Success = false, Message = "TransactionFactory not initialized" };

            // 1. ЗАПОМИНАЕМ СОСТОЯНИЕ (Snapshot)
            // Фиксируем список ключей до начала выполнения команды
            var storageSnapshot = AtomicStorage.GetCurrentContext();

            using (_currentHandler = TransactionFactory(name))
            {
                try
                {
                    var result = action();

                    if (result != null && result.Success)
                    {
                        _currentHandler.Assimilate(); // Фиксация в Revit
                    }
                    else
                    {
                        _currentHandler.Rollback();  // Откат в Revit
                        RollbackStorage(storageSnapshot); // ОТКАТ В ПАМЯТИ
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    _currentHandler.Rollback();
                    RollbackStorage(storageSnapshot); // ОТКАТ В ПАМЯТИ при краше

                    Debug.WriteLine($"[TransactionManager] Rollback due to: {ex.Message}");
                    return new AtomicResult { Success = false, Message = ex.Message };
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