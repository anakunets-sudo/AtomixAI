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

        public static AtomicResult ExecuteSequence(string name, Func<AtomicResult> sequenceAction)
        {
            // Снимок хранилища меток (#) до начала
            var snapshot = AtomicStorage.GetCurrentContext();

            using (var handler = TransactionFactory?.Invoke(name))
            {
                if (handler == null) return AtomicResult.Error("Factory not initialized");
                _currentHandler = handler;

                try
                {
                    // 1. ВЫПОЛНЕНИЕ: Получаем один склеенный результат из DispatchSequence
                    var finalResult = sequenceAction();

                    // 2. РЕАКЦИЯ: Если хоть один шаг внутри был Success = false
                    if (finalResult == null || !finalResult.Success)
                    {
                        handler.Rollback(); // Откатываем все изменения в Revit
                        RollbackStorage(snapshot); // Очищаем созданные в этой сессии метки

                        // Возвращаем тот самый Error, который пришел из Dispatcher
                        return finalResult ?? AtomicResult.Error("Sequence failed with null result.");
                    }

                    // 3. ФИКСАЦИЯ: Если всё отлично
                    handler.Assimilate();
                    return finalResult;
                }
                catch (Exception ex)
                {
                    handler?.Rollback();
                    RollbackStorage(snapshot);
                    return AtomicResult.Error($"Sequence Critical Crash: {ex.Message}");
                }
                finally
                {
                    _currentHandler = null;
                }
            }
        }

        /*
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

        */
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
        /*
        public static AtomicResult ExecuteSafe(string name, Action action)
        {
            return ExecuteSafe(name, () => {
                action();
                return new AtomicResult { Success = true };
            });
        }*/
    }
}