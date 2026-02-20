using AtomixAI.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Main.Infrastructure
{
    public class RevitTransactionHandler : ITransactionHandler
    {
        private readonly TransactionGroup _transaction;

        public UIDocument UIDoc { get; }

        public UIApplication UIApp { get; }

        public RevitTransactionHandler(UIDocument uiDoc, string name)
        {
            UIDoc = uiDoc; 
            UIApp = uiDoc.Application;
            _transaction = new TransactionGroup(UIDoc.Document, name);
            _transaction.Start();
        }

        public void Assimilate() => _transaction.Assimilate();
        public void Rollback() => _transaction.RollBack();

        public void Dispose()
        {
            if (_transaction.GetStatus() == TransactionStatus.Started)
            {
                _transaction.RollBack();
            }
            _transaction.Dispose();
        }
    }
}
