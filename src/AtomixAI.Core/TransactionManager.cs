using AtomixAI.Core;
using System;
using System.Diagnostics;

namespace AtomixAI.Core
{
    public static class TransactionManager
    {
        public static Func<string, ITransactionHandler>? TransactionFactory { get; set; }

        // Храним текущий хендлер для доступа из команд 
        [ThreadStatic]
        private static ITransactionHandler? _currentHandler;

        public static ITransactionHandler? CurrentHandler => _currentHandler;

        public static AtomicResult ExecuteSafe(string name, Func<AtomicResult> action)
        {
            if (TransactionFactory == null) return new AtomicResult { Success = false };

            using (_currentHandler = TransactionFactory(name))
            {
                try
                {
                    // Выполняем и сохраняем результат команды!
                    var result = action();

                    if (result.Success) _currentHandler.Assimilate();
                    else _currentHandler.Rollback();

                    return result;
                }
                catch (Exception ex)
                {
                    _currentHandler.Rollback();
                    Debug.WriteLine($"[TransactionManager] Rollback due to: {ex.Message}");
                    return new AtomicResult { Success = false, Message = ex.Message };
                }
                finally { _currentHandler = null; }
            }
        }


        public static AtomicResult ExecuteSafe(string name, Action action)
        {
            if (TransactionFactory == null)
                return new AtomicResult { Success = false };

            using (_currentHandler = TransactionFactory(name))
            {
                try
                {
                    action();
                    _currentHandler.Assimilate();
                    return new AtomicResult { Success = true };
                }
                catch (Exception ex)
                {
                    _currentHandler.Rollback();
                    throw; // Пробрасываем выше для Dispatcher 
                }
                finally
                {
                    _currentHandler = null;
                }
            }
        }
    }
}