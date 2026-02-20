using AtomixAI.Bridge;
using AtomixAI.Core;
using Autodesk.Revit.UI;
using System;
using System.Threading.Tasks;

namespace AtomixAI.Main
{
    public class App : IExternalApplication
    {
        private McpHost _mcpHost;
        private ExternalEvent _externalEvent;
        private Infrastructure.AtomicExternalEventHandler _handler;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 1. Инициализируем ToolDispatcher (укажите ваш путь к папке со скриптами Python)
                var dispatcher = new ToolDispatcher(@"C:\AtomixAI\Scripts");

                // 2. Создаем обработчик и регистрируем ExternalEvent
                _handler = new Infrastructure.AtomicExternalEventHandler(dispatcher);
                _externalEvent = ExternalEvent.Create(_handler);

                // 3. Запускаем MCP Host
                _mcpHost = new McpHost(_handler.CommandQueue, _externalEvent);

                Task.Run(async () => {
                    try
                    {
                        await _mcpHost.ListenAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CRITICAL] MCP Host failed: {ex.Message}");
                    }
                });

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MessageBox.Show("AtomixAI Load Error", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            _mcpHost?.Stop();
            return Result.Succeeded;
        }
    }
}