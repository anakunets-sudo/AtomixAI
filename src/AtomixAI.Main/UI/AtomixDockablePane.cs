using AtomixAI.Bridge;
using AtomixAI.Core;
using Autodesk.Revit.UI;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media;

namespace AtomixAI.Main.UI
{ // Это наш корневой элемент панели. Никакого .xaml файла!
    public class AtomixDockablePane : System.Windows.Controls.Page, Autodesk.Revit.UI.IDockablePaneProvider
    {
        public Microsoft.Web.WebView2.Wpf.WebView2 WebView { get; private set; }
        private readonly Infrastructure.AtomicExternalEventHandler _handler;
        private readonly Autodesk.Revit.UI.ExternalEvent _exEvent;
        private bool _isInitializing = false; // Поле в классе для защиты от дублей
        private McpHost _mcpHost;

        // НАШИ НОВЫЕ ПОЛЯ ДЛЯ ИЗОЛИРОВАННОГО UI
        private readonly AtomixAI.UI.Infrastructure.UiExternalEventHandler _uiHandler;
        private readonly Autodesk.Revit.UI.ExternalEvent _uiExEvent;

        public async System.Threading.Tasks.Task InitializeAsync(UIApplication uiapp = null, string customUrl = null)
        { // 1. Если уже инициализировано или в процессе — выходим
            if (WebView.CoreWebView2 != null || _isInitializing) return;
            _isInitializing = true;
            try
            {
                ApplyTheme(uiapp);
                // 2. Настройка папки кэша (UserDataFolder)
                string folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomixAI", "WebView_Cache");
                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
                // 3. Создаем окружение
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, folder);
                // КРИТИЧНО: Сначала привязываем окружение, и ТОЛЬКО ПОТОМ задаем Source
                await WebView.EnsureCoreWebView2Async(env);

                // Связываем наш обработчик с WebView и диспетчером WPF-страницы
                _uiHandler.InitializeBridge(WebView, this.Dispatcher);

                // 4. Подписываемся на события (только один раз!)
                WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                // Схема цвета ДО навигации, иначе первая отрисовка HTML всегда light
                ApplyTheme(uiapp);
                
                // Передаем текущую тему во фронтенд сразу после того, как он загрузится
                WebView.NavigationCompleted += (sender, args) =>
                {
                    PushThemeToFrontend();
                };

                // 5. Установка адреса
                if (!string.IsNullOrEmpty(customUrl))
                {
                    WebView.Source = new Uri(customUrl);
                }
                else
                {
                    string assemblyDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string htmlPath = System.IO.Path.Combine(assemblyDir, "wwwroot", "index.html");
                    if (System.IO.File.Exists(htmlPath)) WebView.Source = new Uri(htmlPath);
                    else WebView.NavigateToString("<h2 style='color:red;'>AtomixAI: wwwroot/index.html not found!</h2>");
                }

#if RELEASE
                // В готовом плагине для пользователей всё блокируем
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#else
                // При разработке (Debug) оставляем меню и консоль включенными
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
#endif


            }
            catch (Exception ex)
            { // Теперь мы увидим реальную причину, если она в чем-то другом
                System.Diagnostics.Debug.WriteLine($"WebView2 Error: {ex.Message}");
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private bool _isDarkTheme = false;
        private bool _hostThemeKnown = false;
        private bool _nativeSchemeApplied = false;

        public AtomixDockablePane(Infrastructure.AtomicExternalEventHandler handler, Autodesk.Revit.UI.ExternalEvent exEvent, AtomixAI.UI.Infrastructure.UiExternalEventHandler uiHandler, ExternalEvent uiExEvent,  McpHost mcpHost)
        {
            _handler = handler;
            _exEvent = exEvent;

            // Создаем изолированный обработчик для UI
            _uiHandler = uiHandler;
            _uiExEvent = uiExEvent;

            _mcpHost = mcpHost; // Чистый C# Layout
            var grid = new Grid { Background = System.Windows.Media.Brushes.Transparent };
            WebView = new Microsoft.Web.WebView2.Wpf.WebView2();
            grid.Children.Add(WebView);
            this.Content = grid;
        }

        private static bool ResolveIsDark(UIApplication uiapp)
        {
#if REVIT2024_OR_GREATER
            return UIThemeManager.CurrentTheme == UITheme.Dark;
#else
            if (uiapp?.Application == null) return false;
            var col = uiapp.Application.BackgroundColor;
            return (0.299 * col.Red + 0.587 * col.Green + 0.114 * col.Blue) < 128;
#endif
        }

        public void ApplyTheme(UIApplication uiapp)
        {
            bool isDark = ResolveIsDark(uiapp);
            bool themeChanged = !_hostThemeKnown || _isDarkTheme != isDark;
            _hostThemeKnown = true;
            _isDarkTheme = isDark;

            var darkBg = System.Windows.Media.Color.FromRgb(59, 68, 83); // Revit dark chrome #3B4453
            if (this.Content is Grid g)
            {
                g.Background = isDark ? new SolidColorBrush(darkBg) : System.Windows.Media.Brushes.White;
            }

            if (WebView != null)
            {
                WebView.DefaultBackgroundColor = isDark
                    ? System.Drawing.Color.FromArgb(59, 68, 83)
                    : System.Drawing.Color.White;
            }

            if (WebView?.CoreWebView2 == null)
                return;

            if (themeChanged || !_nativeSchemeApplied)
            {
                WebView.CoreWebView2.Profile.PreferredColorScheme = isDark
                    ? CoreWebView2PreferredColorScheme.Dark
                    : CoreWebView2PreferredColorScheme.Light;
                _nativeSchemeApplied = true;
                PushThemeToFrontend();
            }
        }

        private void PushThemeToFrontend()
        {
            if (WebView?.CoreWebView2 == null) return;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                action = "theme_changed",
                theme = _isDarkTheme ? "dark" : "light"
            });
            WebView.CoreWebView2.PostWebMessageAsJson(json);
        }
        public void SetupDockablePane(Autodesk.Revit.UI.DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new Autodesk.Revit.UI.DockablePaneState
            {
                DockPosition = Autodesk.Revit.UI.DockPosition.Tabbed,
                TabBehind = Autodesk.Revit.UI.DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };
        }
        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var request = Newtonsoft.Json.Linq.JObject.Parse(e.WebMessageAsJson);
                string action = request["action"]?.ToString();
                switch (action)
                {
                    case "GET_SUB_CONTEXT":
                        string subId = request["payload"]?["id"]?.ToString();
                        if (!string.IsNullOrEmpty(subId))
                        {
                            lock (_uiHandler.UiCommandQueue)
                            {
                                // Складываем в UI-очередь и будим UI ExternalEvent
                                _uiHandler.UiCommandQueue.Enqueue(("GET_SUB_CONTEXT", subId));
                            }
                            _uiExEvent.Raise(); // Будим выделенный поток Revit для UI
                        }
                        break;

                    case "toggle_voice":
                        bool isActive = (bool)request["active"]; // Мгновенная активация Python через UDP
                        using (UdpClient udp = new UdpClient())
                        {
                            byte[] data = Encoding.UTF8.GetBytes(isActive ? "1" : "0");
                            udp.Send(data, data.Length, "127.0.0.1", 5006);
                        }
                        break;
                    case "chat_request":
                        // 1. Распаковываем наш безопасный payload-контур
                        var payload = request["payload"];
                        if (payload != null)
                        {
                            string prompt = payload["prompt"]?.ToString();
                            var context = payload["context"] as Newtonsoft.Json.Linq.JArray;

                            // 2. ДИНАМИЧЕСКИЙ ПРОГРЕВ ХРАНИЛИЩА (Только если чипсы были переданы)
                            if (context != null && context.Count > 0)
                            {
                                foreach (var chipToken in context)
                                {
                                    string alias = chipToken["alias"]?.ToString(); // Извлекаем хэштег, например "#Высота"

                                    if (!string.IsNullOrEmpty(alias))
                                    {
                                        // Кладём в AtomicStorage весь JSON-паспорт чипса целиком в виде JObject.
                                        // ИИ никогда не увидит эти внутренности, но ToolDispatcher распакует их при маппинге.
                                        AtomicStorage.Set(alias, chipToken);
                                        System.Diagnostics.Debug.WriteLine($"[Storage Pre-heat] Registered chip: {alias}");
                                    }
                                }
                            }

                            // 3. ПЕРЕДАЧА ИИ: Отправляем очищенный текст промпта асинхронному MCP-клиенту
                            if (!string.IsNullOrEmpty(prompt))
                            {
                                System.Diagnostics.Debug.WriteLine($"[UI Debug] Prompt sent to AI: {prompt}");

                                var pipePayload = Newtonsoft.Json.JsonConvert.SerializeObject(new
                                {
                                    action = "chat_request",
                                    prompt = prompt
                                });
                                _mcpHost.BroadcastToClients(pipePayload);
                            }
                        }
                        break;

                    case "call":
                        string toolName = request["name"]?.ToString();
                        string args = request["args"]?.ToString();
                        if (!string.IsNullOrEmpty(toolName))
                        {
                            lock (_handler.CommandQueue)
                            {
                                _handler.CommandQueue.Enqueue((toolName, args));
                            }
                            _exEvent.Raise();
                        }
                        break;
                    case "stop":
                        lock (_handler.CommandQueue)
                        {
                            _handler.CommandQueue.Clear();
                        }
                        _mcpHost.BroadcastToClients(Newtonsoft.Json.JsonConvert.SerializeObject(new { action = "abort" }));
                        AtomixAI.Core.TransactionManager.CurrentHandler?.Rollback();
                        break;
                    case "rate_training": // Твоя логика сохранения оценок
                        break;
                    case "theme_manual":
                        string manualTheme = request["theme"]?.ToString(); // Вызываем покраску WPF контейнера
                        this.Dispatcher.Invoke(() =>
                        {
                            if (this.Content is Grid g)
                            {
                                g.Background = manualTheme == "dark" ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 68, 83)) : System.Windows.Media.Brushes.White;
                            }
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebView Bridge Error]: {ex.Message}");
            }
        }

        public static DockablePaneId ID => new DockablePaneId(new Guid("704D02EE-A17E-4A10-B53C-3DA5E86FE758"));

        public static string Name => "BIM Wave";
    }
}

