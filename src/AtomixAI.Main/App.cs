using AtomixAI.Bridge;
using AtomixAI.Main.Infrastructure;
using AtomixAI.Main.UI;
using Autodesk.Revit.UI;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Windows.Threading;

namespace AtomixAI.Main
{
    public class App : IExternalApplication
    {
        private McpHost _mcpHost;
        private ExternalEvent _externalEvent;
        private Infrastructure.AtomicExternalEventHandler _handler;
        private AtomixDockablePane _pane;
        public static readonly DockablePaneId PaneId = AtomixDockablePane.ID;
        private System.Diagnostics.Process _pyProcess;
        private System.Diagnostics.Process _vocalSyncProcess;
        private CancellationTokenSource? _vocalSyncCts;
        private NamedPipeServerStream? _vocalPipe;
        private readonly object _vocalPipeGate = new object();

        // НАШИ НОВЫЕ ПОЛЯ ДЛЯ ИЗОЛИРОВАННОГО UI
        private AtomixAI.UI.Infrastructure.UiExternalEventHandler _uiHandler;
        private Autodesk.Revit.UI.ExternalEvent _uiExEvent;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string i18nDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "wwwroot", "i18n");
                var uiCulture = System.Threading.Thread.CurrentThread.CurrentUICulture;
                AtomixAI.Core.Localizer.Initialize(i18nDir, uiCulture);
                Debug.WriteLine($"[i18n] CurrentUICulture={uiCulture.Name}, loaded={AtomixAI.Core.Localizer.Language}");

                //if (!System.Diagnostics.Debugger.IsAttached)
                {
                    StartEmbeddedOrchestrator();
                }
                // 1. Инициализируем ToolDispatcher (укажите ваш путь к папке со скриптами Python)
                var _dispatcher = new ToolDispatcher(@"C:\AtomixAI\Scripts");
                // 2. Создаем обработчик (пока без хоста, чтобы избежать ошибки конструктора)
                _handler = new AtomicExternalEventHandler(_dispatcher);
                _externalEvent = ExternalEvent.Create(_handler);

                // Создаем изолированный обработчик для UI
                _uiHandler = new AtomixAI.UI.Infrastructure.UiExternalEventHandler();
                _uiExEvent = ExternalEvent.Create(_uiHandler);

                // 3. Создаем MCP Host, передавая ему очередь из обработчика
                _mcpHost = new McpHost(_handler.CommandQueue, _externalEvent, _dispatcher);
                _dispatcher.RegisterHost(_mcpHost);
                _handler.RegisterHost(_mcpHost);
                // 4. Регистрируем панель, передавая в неё McpHost
                _pane = new AtomixDockablePane(_handler, _externalEvent, _uiHandler, _uiExEvent,  _mcpHost); // ДОБАВЛЕНО: _mcpHost
                application.RegisterDockablePane(PaneId, AtomixDockablePane.Name, _pane);
                // Подписка на ответы от ИИ для проброса в UI
                _mcpHost.OnMessageReceived += (jsonPayload) =>
                {
                    try
                    {
                        // 1. Пытаемся найти Dispatcher через родительское окно WebView или текущий поток
                        var dispatcher = _pane.WebView.Dispatcher;
                        if (dispatcher != null)
                        {
                            dispatcher.Invoke(() =>
                            {
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
                Task.Run(async () =>
                {
                    try
                    {
                        await _mcpHost.ListenAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CRITICAL] MCP Host failed: {ex.Message}");
                    }
                });


                // Один проход инициализации WebView + несколько idle, пока Revit дотягивает CurrentTheme
                EventHandler<Autodesk.Revit.UI.Events.IdlingEventArgs> webViewBoot = null;
                var initStarted = false;
                var themeSettleCount = 0;
                webViewBoot = (s, e) =>
                {
                    var uiapp = s as UIApplication;
                    if (!initStarted)
                    {
                        initStarted = true;
                        _pane.ApplyTheme(uiapp);
                        _ = BootWebViewAsync(uiapp);
                        return;
                    }

                    if (_pane.WebView.CoreWebView2 == null)
                        return;

                    _pane.ApplyTheme(uiapp);
                    if (++themeSettleCount >= 12)
                        application.Idling -= webViewBoot;
                };
                application.Idling += webViewBoot;
#if REVIT2024_OR_GREATER
            application.ThemeChanged += (s, e) =>
            {
                if (s is UIApplication uiapp)
                {
                    _pane?.ApplyTheme(uiapp);
                }
            };
#endif
                CreateRibbon(application);
                StartVocalSyncServer(_pane);
                StartVocalSyncProcess();
                return Result.Succeeded;
            }
            catch (Exception ex)
            { //MessageBox.Show("AtomixAI Load Error", ex.Message);
                return Result.Failed;
            }
        }

        private async System.Threading.Tasks.Task BootWebViewAsync(UIApplication uiapp)
        {
            try
            {
                await _pane.InitializeAsync(uiapp);
                await _pane.Dispatcher.InvokeAsync(() => _pane.ApplyTheme(uiapp));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView Boot] {ex.Message}");
            }
        }
        public Result OnShutdown(UIControlledApplication application)
        {
            _mcpHost?.Stop();
            StopVocalSyncServer();
            try
            {
                if (_pyProcess != null && !_pyProcess.HasExited) _pyProcess.Kill();
                if (_vocalSyncProcess != null && !_vocalSyncProcess.HasExited) _vocalSyncProcess.Kill();
            }
            catch
            { /* Handle exit race conditions */
            }
            return Result.Succeeded;
        }
        private void CreateRibbon(UIControlledApplication a)
        {
            string tabName = "AtomicBIM";
            try
            {
                a.CreateRibbonTab(tabName);
            }
            catch
            {
            } // Создаем вкладку, если её нет
            RibbonPanel panel = a.CreateRibbonPanel(tabName, AtomixAI.Core.Localizer.T("ribbon.panel"));
            // Путь к текущей DLL
            string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            // Создаем кнопку, которая вызывает наш класс ShowAiPane (из Command.cs)
            PushButtonData btnData = new PushButtonData(
            "Show Pane", AtomixAI.Core.Localizer.T("ribbon.openChat"), assemblyPath,
            "AtomixAI.Main.ShowPane" // Полное имя класса с пространством имен!
            );
            PushButton btn = panel.AddItem(btnData) as PushButton;
            btn.ToolTip = AtomixAI.Core.Localizer.T("ribbon.openChat.tooltip"); // Можно добавить иконку (32x32)
                                                       // btn.LargeImage = new BitmapImage(new Uri("pack://application:,,,/YourAssembly;component/Resources/ai_icon.png"));
        }
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
                // ЖЕСТКИЙ ХАК ДЛЯ PYTHON 3.7+: Включаем глобальный режим UTF-8 на уровне процесса Windows
                startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
                // API key управляется непосредственно в Python-оркестраторе.
                // Здесь он только передаётся как аргумент запуска, если найден в окружении пользователя/машины.
                string openRouterKey =
                    Environment.GetEnvironmentVariable("OPENROUTER_API_KEY", EnvironmentVariableTarget.User)
                    ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY", EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(openRouterKey))
                {
                    string safeKey = openRouterKey.Replace("\"", "\\\"");
                    startInfo.Arguments = $"\"{scriptPath}\" --api-key \"{safeKey}\"";
                }
                else
                {
                    startInfo.Arguments = $"\"{scriptPath}\"";
                    System.Diagnostics.Debug.WriteLine("[AtomixAI] OPENROUTER_API_KEY is not set in User/Machine environment; orchestrator will read it from its own config/env fallback.");
                }
                _pyProcess = new System.Diagnostics.Process { StartInfo = startInfo };
                _pyProcess.Start();
                ProcessJobTracker.AddProcess(_pyProcess);
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
                ProcessJobTracker.AddProcess(_vocalSyncProcess);
                Debug.WriteLine("[AtomixAI] VocalSync Engine Started.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VocalSyncLaunchError]: {ex.Message}");
            }
        }
        private void StartVocalSyncServer(AtomixDockablePane pane)
        {
            _vocalSyncCts = new CancellationTokenSource();
            var token = _vocalSyncCts.Token;

            Task.Run(async () =>
            {
                // Переподключение после закрытия pipe клиентом. Выход — по отмене в OnShutdown.
                while (!token.IsCancellationRequested)
                {
                    NamedPipeServerStream? pipeServer = null;
                    try
                    {
                        // Asynchronous: иначе WaitForConnectionAsync(token) не снимается при отмене.
                        pipeServer = new NamedPipeServerStream(
                            "AtomixAI_Vocal_Pipe",
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        lock (_vocalPipeGate)
                        {
                            if (token.IsCancellationRequested)
                                break;
                            _vocalPipe = pipeServer;
                        }

                        await pipeServer.WaitForConnectionAsync(token);
                        if (token.IsCancellationRequested)
                            break;

                        using (var reader = new StreamReader(pipeServer))
                        {
                            while (!token.IsCancellationRequested && !reader.EndOfStream)
                            {
                                var text = await reader.ReadLineAsync();
                                if (text == null)
                                    break;
                                if (text.Length == 0)
                                    continue;

                                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
                                {
                                    type = "voice_input",
                                    content = text
                                });
                                pane.Dispatcher.Invoke(() =>
                                {
                                    pane.WebView?.CoreWebView2?.PostWebMessageAsJson(json);
                                });
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (token.IsCancellationRequested)
                            break;

                        Debug.WriteLine($"[VocalSyncPipe]: {ex.Message}");
                        try
                        {
                            // Без паузы сбой создания pipe крутит цикл и занимает ядро.
                            await Task.Delay(500, token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        if (pipeServer != null)
                        {
                            lock (_vocalPipeGate)
                            {
                                if (ReferenceEquals(_vocalPipe, pipeServer))
                                    _vocalPipe = null;
                            }

                            try
                            {
                                if (pipeServer.IsConnected)
                                    pipeServer.Disconnect();
                                pipeServer.Dispose();
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            });
        }

        private void StopVocalSyncServer()
        {
            CancellationTokenSource? cts;
            NamedPipeServerStream? pipe;
            lock (_vocalPipeGate)
            {
                cts = _vocalSyncCts;
                pipe = _vocalPipe;
                _vocalPipe = null;
            }

            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            // Dispose снимает и WaitForConnectionAsync, и ReadLineAsync, если отмена токена их не будит.
            try
            {
                pipe?.Dispose();
            }
            catch
            {
            }
        }
    }
}