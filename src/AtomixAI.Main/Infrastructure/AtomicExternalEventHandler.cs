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
            TransactionManager.TransactionFactory = (name) => new RevitTransactionHandler(app.ActiveUIDocument, name);

            while (CommandQueue.Count > 0)
            {
                var task = CommandQueue.Dequeue();
                AtomicResult finalResult = null;

                if (task.ToolId == "__BATCH__")
                {
                    // DispatchSequence вернет List<AtomicResult> внутри одного AtomicResult.Data
                    finalResult = _dispatcher.DispatchSequence(task.JsonArgs);
                }
                /*else
                {
                    // Одиночная команда
                    finalResult = _dispatcher.Dispatch(task.ToolId, task.JsonArgs);
                }*/

                // ЕДИНАЯ ТОЧКА ОТЧЕТА:
                // Теперь ИИ получает ровно ОДИН ответ на свой ОДИН запрос (будь то call или call_batch)
                _mcpHost?.SendToolResult(finalResult, task.ToolId);
            }
        }

        public string GetName() => "AtomixAI_Main_ExternalEvent";
    }
}
