using AtomixAI.Core;
using System;

namespace AtomixAI.Core
{
    public static class TransactionManager
    {
        public static Func<string, ITransactionHandler>? TransactionFactory { get; set; }

        // Храним текущий хендлер для доступа из команд 
        [ThreadStatic]
        private static ITransactionHandler? _currentHandler;

        public static ITransactionHandler? CurrentHandler => _currentHandler;

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