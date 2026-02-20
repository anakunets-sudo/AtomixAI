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
        // Очередь команд: ID инструмента и его JSON-аргументы 
        public readonly Queue<(string ToolId, string JsonArgs)> CommandQueue = new Queue<(string, string)>();
        private readonly ToolDispatcher _dispatcher;

        public AtomicExternalEventHandler(ToolDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void Execute(UIApplication app)
        {
            var iuDoc = app.ActiveUIDocument;
            if (iuDoc == null) return;

            // Временно переопределяем фабрику прямо здесь, где есть живой doc 
            TransactionManager.TransactionFactory = (name) => new RevitTransactionHandler(iuDoc, name);

            while (CommandQueue.Count > 0)
            {
                var (toolId, jsonArgs) = CommandQueue.Dequeue();

                // 1. Пытаемся выполнить команду 
                var result = _dispatcher.Dispatch(toolId, jsonArgs);

                // 2. ВЫВОДИМ РЕЗУЛЬТАТ: если Success == false, здесь будет описание ошибки 
                if (!result.Success)
                {
                    /*MessageBox.Show( 
                        $"Команда: {toolId}\n" + 
                        $"Ошибка: {result.Message}\n" + 
                        $"Данные: {jsonArgs}" 
                        , "AtomixAI Error");*/
                }
                else
                {
                    // Если всё ок, но MessageBox в WallCreateCmd молчит — значит логика внутри TransactionManager 
                    //MessageBox.Show("AtomixAI Success", $"Команда {toolId} выполнена."); 
                }
            }
        }

        public string GetName() => "AtomixAI_Main_ExternalEvent";
    }
}
