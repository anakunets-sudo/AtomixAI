using AtomixAI.Bridge;
using AtomixAI.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Net.Sockets;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media;

namespace AtomixAI.Main.UI
{
    // Это наш корневой элемент панели. Никакого .xaml файла!
    public class AtomixDockablePane : System.Windows.Controls.Page, Autodesk.Revit.UI.IDockablePaneProvider
    {
        public WebView2 WebView { get; private set; }
        private readonly Infrastructure.AtomicExternalEventHandler _handler;
        private readonly Autodesk.Revit.UI.ExternalEvent _exEvent;
        private bool _isInitializing = false; // Поле в классе для защиты от дублей
        private McpHost _mcpHost;

        public async System.Threading.Tasks.Task InitializeAsync(string customUrl = null)
        {
            // 1. Если уже инициализировано или в процессе — выходим
            if (WebView.CoreWebView2 != null || _isInitializing) return;

            _isInitializing = true;
            try
            {
                // 2. Настройка папки кэша (UserDataFolder)
                string folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtomixAI", "WebView_Cache");

                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);

                // 3. Создаем окружение
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, folder);

                // КРИТИЧНО: Сначала привязываем окружение, и ТОЛЬКО ПОТОМ задаем Source
                await WebView.EnsureCoreWebView2Async(env);

                // 4. Подписываемся на события (только один раз!)
                WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                // 5. Установка адреса
                if (!string.IsNullOrEmpty(customUrl))
                {
                    WebView.Source = new Uri(customUrl);
                }
                else
                {
                    string assemblyDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string htmlPath = System.IO.Path.Combine(assemblyDir, "wwwroot", "index.html");

                    if (System.IO.File.Exists(htmlPath))
                        WebView.Source = new Uri(htmlPath);
                    else
                        WebView.NavigateToString("<h2 style='color:red;'>AtomixAI: wwwroot/index.html not found!</h2>");
                }
            }
            catch (Exception ex)
            {
                // Теперь мы увидим реальную причину, если она в чем-то другом
                System.Diagnostics.Debug.WriteLine($"WebView2 Error: {ex.Message}");
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public AtomixDockablePane(Infrastructure.AtomicExternalEventHandler handler, Autodesk.Revit.UI.ExternalEvent exEvent, McpHost mcpHost)
        {
            _handler = handler;
            _exEvent = exEvent;
            _mcpHost = mcpHost;

            // Чистый C# Layout
            var grid = new Grid { Background = System.Windows.Media.Brushes.Transparent };
            WebView = new WebView2();
            grid.Children.Add(WebView);
            this.Content = grid;
        }
        public void ApplyTheme(Autodesk.Revit.UI.UIApplication uiapp)
        {
            bool isDark = false;
#if REVIT2025_OR_GREATER
    isDark = Autodesk.Revit.UI.UIThemeManager.CurrentTheme == Autodesk.Revit.UI.UITheme.Dark;
#else
            // Для 2019-2024: проверяем яркость фона чертежа
            var col = uiapp.Application.BackgroundColor;
            isDark = (0.299 * col.Red + 0.587 * col.Green + 0.114 * col.Blue) < 128;
#endif

            // 1. Красим WPF-подложку (Grid)
            if (this.Content is Grid g)
                g.Background = isDark ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 43, 43)) : System.Windows.Media.Brushes.White;

            // 2. Отправляем JSON во фронтенд
            if (WebView?.CoreWebView2 != null)
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    action = "theme_changed",
                    theme = isDark ? "dark" : "light"
                });
                WebView.CoreWebView2.PostWebMessageAsJson(json);
            }
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
                    case "toggle_voice":
                        bool isActive = (bool)request["active"];
                        // Мгновенная активация Python через UDP
                        using (UdpClient udp = new UdpClient())
                        {
                            byte[] data = Encoding.UTF8.GetBytes(isActive ? "1" : "0");
                            udp.Send(data, data.Length, "127.0.0.1", 5006);
                        }
                        break;

                    case "chat_request":
                        string prompt = request["prompt"]?.ToString();
                        if (!string.IsNullOrEmpty(prompt))
                        {
                            // ЛОГ ДЛЯ ПРОВЕРКИ:
                            System.Diagnostics.Debug.WriteLine($"[UI Debug]: Получен промпт: {prompt}");

                            var pipePayload = Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                action = "chat_request", // убедись, что экшен совпадает с тем, что ждет Python
                                prompt = prompt
                            });
                            _mcpHost.BroadcastToClients(pipePayload);
                        }
                        break;

                    case "call":
                        string toolName = request["name"]?.ToString();
                        string args = request["args"]?.ToString();
                        if (!string.IsNullOrEmpty(toolName))
                        {
                            lock (_handler.CommandQueue) { _handler.CommandQueue.Enqueue((toolName, args)); }
                            _exEvent.Raise();
                        }
                        break;

                    case "stop":
                        lock (_handler.CommandQueue) { _handler.CommandQueue.Clear(); }
                        _mcpHost.BroadcastToClients(Newtonsoft.Json.JsonConvert.SerializeObject(new { action = "abort" }));
                        AtomixAI.Core.TransactionManager.CurrentHandler?.Rollback();
                        break;

                    case "rate_training":
                        // Твоя логика сохранения оценок
                        break;

                    case "theme_manual":
                        string manualTheme = request["theme"]?.ToString();
                        // Вызываем покраску WPF контейнера
                        this.Dispatcher.Invoke(() =>
                        {
                            if (this.Content is Grid g)
                            {
                                g.Background = manualTheme == "dark"
                                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 43, 43))
                                    : System.Windows.Media.Brushes.White;
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
    }
}

