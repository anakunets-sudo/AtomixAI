using AtomixAI.Bridge;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AtomixAI.Core;
using System.Diagnostics;

namespace AtomixAI.Main.Infrastructure
{    
    public class AtomicExternalEventHandler : IExternalEventHandler
    {
        // Очередь команд: инструмент, JSON-аргументы и поколение запроса чата.
        public readonly Queue<PendingToolCall> CommandQueue = new Queue<PendingToolCall>();

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

            while (true)
            {
                PendingToolCall task;
                lock (CommandQueue)
                {
                    if (CommandQueue.Count == 0)
                        break;
                    task = CommandQueue.Dequeue();
                }

                // __BATCH__: JsonArgs — массив шагов {name, arguments}.
                // Одиночный call: ToolId — имя инструмента, JsonArgs — объект аргументов.
                AtomicResult finalResult;
                if (string.Equals(task.ToolId, "__BATCH__", StringComparison.Ordinal))
                {
                    finalResult = _dispatcher.DispatchSequence(task.JsonArgs, task.Generation);
                }
                else
                {
                    string jsonArgs = string.IsNullOrWhiteSpace(task.JsonArgs) ? "{}" : task.JsonArgs;
                    finalResult = TransactionManager.ExecuteSequence(
                        task.ToolId,
                        () => _dispatcher.Dispatch(task.ToolId, jsonArgs),
                        task.Generation);
                }

                Debug.WriteLine($"[AtomicExternalEventHandler] finalResult: {finalResult.ToString()}");

                Debug.WriteLine($"[AtomicExternalEventHandler] _mcpHost: {_mcpHost.ToString()}");

                _mcpHost?.SendToolResult(finalResult, task.ToolId, task.Generation);

                Debug.WriteLine($"[AtomicExternalEventHandler] ended ");
            }
        }

        public string GetName() => "AtomixAI_Main_ExternalEvent";
    }
}
