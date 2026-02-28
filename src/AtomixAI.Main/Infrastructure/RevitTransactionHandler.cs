using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AtomixAI.Core;
using System.Diagnostics;

namespace AtomixAI.Main.Infrastructure
{
    /// <summary>
    /// Реализация обработчика транзакций для Revit.
    /// Оборачивает выполнение в TransactionGroup для поддержки откатов цепочек (Sequences).
    /// </summary>
    public class RevitTransactionHandler : ITransactionHandler
    {
        private TransactionGroup _group;
        public UIDocument UIDoc { get; }
        public UIApplication UIApp => UIDoc?.Application;

        public RevitTransactionHandler(UIDocument uiDoc, string name)
        {
            UIDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));

            // Инициализируем группу транзакций. 
            // Все Transaction внутри команд (WallCreate и т.д.) будут вложены в эту группу.
            _group = new TransactionGroup(UIDoc.Document, name);

            try
            {
                _group.Start();
                Debug.WriteLine($"[RevitTransactionHandler] TransactionGroup '{name}' started.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RevitTransactionHandler] Failed to start group: {ex.Message}");
            }
        }

        /// <summary>
        /// Успешное завершение: "схлопывает" все внутренние транзакции в одну запись в истории Revit.
        /// </summary>
        public void Assimilate()
        {
            if (_group != null && _group.GetStatus() == TransactionStatus.Started)
            {
                _group.Assimilate();
                Debug.WriteLine("[RevitTransactionHandler] Group assimilated (Success).");
            }
        }

        /// <summary>
        /// Откат: отменяет ВСЕ изменения, сделанные всеми командами в рамках этого хендлера.
        /// </summary>
        public void Rollback()
        {
            if (_group != null && _group.GetStatus() == TransactionStatus.Started)
            {
                _group.RollBack();
                Debug.WriteLine("[RevitTransactionHandler] Group rolled back (Failure).");
            }
        }

        /// <summary>
        /// Очистка ресурсов. Если группа не была закрыта (Assimilate/Rollback), 
        /// принудительно откатываем её для безопасности Revit.
        /// </summary>
        public void Dispose()
        {
            if (_group != null)
            {
                if (_group.GetStatus() == TransactionStatus.Started)
                {
                    _group.RollBack();
                }
                _group.Dispose();
                _group = null;
            }
        }
    }
}
