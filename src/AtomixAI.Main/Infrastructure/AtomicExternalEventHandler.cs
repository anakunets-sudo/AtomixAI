using AtomixAI.Bridge;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AtomixAI.Core;

namespace AtomixAI.Main.Infrastructure
{
    public class AtomicExternalEventHandler : IExternalEventHandler
    {
        // Очередь команд: ID инструмента (или маркер __BATCH__) и его JSON-аргументы 
        public readonly Queue<(string ToolId, string JsonArgs)> CommandQueue = new Queue<(string, string)>();

        private readonly ToolDispatcher _dispatcher;
        private McpHost _mcpHost;

        public AtomicExternalEventHandler(ToolDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        // Позволяем App.cs передать ссылку на хост для обратной связи
        public void RegisterHost(McpHost host) => _mcpHost = host;

        public void Execute(UIApplication app)
        {
            // 1. Установка фабрики (чтобы транзакции работали)
            TransactionManager.TransactionFactory = (name) => new RevitTransactionHandler(app.ActiveUIDocument, name);

            while (CommandQueue.Count > 0)
            {
                var task = CommandQueue.Dequeue();
                AtomicResult result;

                if (task.ToolId == "__BATCH__")
                    result = _dispatcher.DispatchSequence(task.JsonArgs); // Вызов цепочки
                else
                    result = _dispatcher.Dispatch(task.ToolId, task.JsonArgs); // Одиночный

                // 2. ОТПРАВКА ОТВЕТА (Если этого нет, Python "зависнет")
                _mcpHost?.SendToolResult(result, task.ToolId);
            }
        }

        public string GetName() => "AtomixAI_Main_ExternalEvent";
    }
}
