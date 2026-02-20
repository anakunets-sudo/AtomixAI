using AtomixAI.Bridge;
using AtomixAI.Core;
using AtomixAI.Main.UI;
using Autodesk.Revit.UI;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AtomixAI.Main
{
    public class App : IExternalApplication
    {
        private McpHost _mcpHost;
        private ExternalEvent _externalEvent;
        private Infrastructure.AtomicExternalEventHandler _handler;
        private AtomixDockablePane _pane;
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("D5CE62F3-154E-43D0-9036-78A4A69215A7"));

        private System.Diagnostics.Process _pyProcess;
        private System.Diagnostics.Process _vocalSyncProcess;
        private void StartEmbeddedOrchestrator()
        {
            try
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                // 1. Путь к нашему встроенному исполняемому файлу
                string pythonExe = Path.Combine(assemblyDir, "PythonRuntime", "python.exe");

                // 2. Путь к скрипту
                string scriptPath = Path.Combine(assemblyDir, "Orchestrator", "orchestrator.py");

                if (!File.Exists(pythonExe))
                {
                    System.Diagnostics.Debug.WriteLine("[AtomixAI] PythonRuntime not found!");
                    return;
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.Combine(assemblyDir, "Orchestrator")
                };

                // Передаем переменные окружения, чтобы Python не искал библиотеки в системе
                startInfo.EnvironmentVariables["PYTHONPATH"] = Path.Combine(assemblyDir, "PythonRuntime");

                _pyProcess = new System.Diagnostics.Process { StartInfo = startInfo };
                _pyProcess.Start();

                // Читаем логи Python в окно Output Visual Studio для отладки
                _pyProcess.BeginOutputReadLine();
                _pyProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PyLaunchError]: {ex.Message}");
            }
        }
        private void StartVocalSyncProcess()
        {
            try
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string pythonExe = Path.Combine(assemblyDir, "PythonRuntime", "python.exe");
                string vocalSyncScript = Path.Combine(assemblyDir, "Orchestrator", "AtomixVocalSync.py");

                if (!File.Exists(vocalSyncScript)) return;

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{vocalSyncScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true, // Скрываем окно, чтобы не мешало в Revit
                    WorkingDirectory = Path.Combine(assemblyDir, "Orchestrator")
                };

                _vocalSyncProcess = new Process { StartInfo = startInfo };
                _vocalSyncProcess.Start();

                Debug.WriteLine("[AtomixAI] VocalSync Engine Started.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VocalSyncLaunchError]: {ex.Message}");
            }
        }
        private void StartVocalSyncServer(AtomixDockablePane pane)
        {
            Task.Run(async () => {
                while (true) // Цикл для переподключения после закрытия Pipe клиентом
                {
                    try
                    {
                        using (var pipeServer = new NamedPipeServerStream("AtomixAI_Vocal_Pipe", PipeDirection.In))
                        {
                            await pipeServer.WaitForConnectionAsync();
                            using (var reader = new StreamReader(pipeServer))
                            {
                                while (!reader.EndOfStream)
                                {
                                    var text = await reader.ReadLineAsync();
                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        // Вбрасываем текст прямо в JS через Dispatcher
                                        pane.Dispatcher.Invoke(() => {
                                            pane.WebView?.CoreWebView2?.ExecuteScriptAsync($"injectVoiceText('{text}')");
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch { /* Ошибка подключения, пробуем снова */ }
                }
            });
        }
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                //if (!System.Diagnostics.Debugger.IsAttached)
                {
                    StartEmbeddedOrchestrator();
                }

                // 1. Инициализируем ToolDispatcher (укажите ваш путь к папке со скриптами Python)
                var dispatcher = new ToolDispatcher(@"C:\AtomixAI\Scripts");

                // 2. Создаем обработчик и регистрируем ExternalEvent
                _handler = new Infrastructure.AtomicExternalEventHandler(dispatcher);
                _externalEvent = ExternalEvent.Create(_handler);

                // 3. Запускаем MCP Host
                _mcpHost = new McpHost(_handler.CommandQueue, _externalEvent, dispatcher);

                // 4. Регистрируем панель, передавая в неё McpHost
                _pane = new AtomixDockablePane(_handler, _externalEvent, _mcpHost); // ДОБАВЛЕНО: _mcpHost
                application.RegisterDockablePane(PaneId, "AtomixAI v1.0", _pane);

                // Подписка на ответы от ИИ для проброса в UI
                _mcpHost.OnMessageReceived += (jsonPayload) => {
                    try
                    {
                        // 1. Пытаемся найти Dispatcher через родительское окно WebView или текущий поток
                        var dispatcher = _pane.WebView.Dispatcher;

                        if (dispatcher != null)
                        {
                            dispatcher.Invoke(() => {
                                if (_pane.WebView.CoreWebView2 != null)
                                {
                                    _pane.WebView.CoreWebView2.PostWebMessageAsJson(jsonPayload);
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UI Push Error]: {ex.Message}");
                    }
                };

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

                // Подписываемся на событие открытия документа, чтобы прогрузить WebView
                application.Idling += (s, e) => {
                    if (_pane.WebView.CoreWebView2 == null)
                    {
                        _pane.InitializeAsync().ContinueWith(t => {
                            // При первой загрузке берем системную тему
                            _pane.Dispatcher.Invoke(() => _pane.ApplyTheme(s as UIApplication));
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    }
                };

#if REVIT2025_OR_GREATER

                application.ThemeChanged += (s, e) => {
                    if (s is UIApplication uiapp)
                    {
                        _pane?.ApplyTheme(uiapp);
                    }
                };
#endif

                StartVocalSyncServer(_pane);

                StartVocalSyncProcess();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                //MessageBox.Show("AtomixAI Load Error", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            _mcpHost?.Stop();
            _pyProcess?.Kill();
            _vocalSyncProcess?.Kill();

            return Result.Succeeded;
        }
    }
}