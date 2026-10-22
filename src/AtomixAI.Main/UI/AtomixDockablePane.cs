using AtomixAI.Bridge;
using AtomixAI.Core;
using Autodesk.Revit.UI;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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
        private const int WmClipboardUpdate = 0x031D;
        private HwndSource? _clipboardSource;
        private bool _clipboardListenerRegistered;

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

                try
                {
                    await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                        $"window.__ATOMIX_I18N = {Localizer.ToJson()}; window.__ATOMIX_LANG = '{Localizer.Language}';");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[i18n] Inject failed, UI stays in English: {ex}");
                }

                // Связываем наш обработчик с WebView и диспетчером WPF-страницы
                _uiHandler.InitializeBridge(WebView, this.Dispatcher);

                // 4. Подписываемся на события (только один раз!)
                WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                // Схема цвета ДО навигации, иначе первая отрисовка HTML всегда light
                ApplyTheme(uiapp);
                
                // Передаем текущую тему во фронтенд сразу после того, как он загрузится
                WebView.NavigationCompleted += async (sender, args) =>
                {
                    PushThemeToFrontend();
                    await PushLocalizationToFrontendAsync();
                    PushClipboardAvailability();
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

        private string? TryReadClipboardHtml()
        {
            try
            {
                return System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.Html)
                    ? System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Html)
                    : string.Empty;
            }
            catch (ExternalException ex)
            {
                Debug.WriteLine($"[Clipboard] Could not read clipboard HTML: {ex.Message}");
                return null;
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"[Clipboard] Could not read clipboard HTML: {ex.Message}");
                return null;
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
            Loaded += OnPaneLoaded;
            Unloaded += OnPaneUnloaded;
        }

        private void OnPaneLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source == null)
            {
                Debug.WriteLine("[Clipboard] Could not find the pane window handle.");
                return;
            }

            if (ReferenceEquals(_clipboardSource, source))
            {
                PushClipboardAvailability();
                return;
            }

            RemoveClipboardListener();
            _clipboardSource = source;
            _clipboardSource.AddHook(OnClipboardWindowMessage);
            _clipboardListenerRegistered = AddClipboardFormatListener(_clipboardSource.Handle);
            if (!_clipboardListenerRegistered)
            {
                Debug.WriteLine($"[Clipboard] AddClipboardFormatListener failed: {Marshal.GetLastWin32Error()}");
            }
            PushClipboardAvailability();
        }

        private void OnPaneUnloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            RemoveClipboardListener();
        }

        private void RemoveClipboardListener()
        {
            if (_clipboardSource == null) return;

            _clipboardSource.RemoveHook(OnClipboardWindowMessage);
            if (_clipboardListenerRegistered && !RemoveClipboardFormatListener(_clipboardSource.Handle))
            {
                Debug.WriteLine($"[Clipboard] RemoveClipboardFormatListener failed: {Marshal.GetLastWin32Error()}");
            }
            _clipboardListenerRegistered = false;
            _clipboardSource = null;
        }

        private IntPtr OnClipboardWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmClipboardUpdate)
            {
                PushClipboardAvailability();
            }
            return IntPtr.Zero;
        }

        private void PushClipboardAvailability()
        {
            string? text = TryReadClipboardText();
            if (text == null || WebView?.CoreWebView2 == null) return;

            WebView.CoreWebView2.PostWebMessageAsJson(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                action = "clipboard_availability_changed",
                available = !string.IsNullOrWhiteSpace(text)
            }));
        }

        private string? TryReadClipboardText()
        {
            try
            {
                return System.Windows.Clipboard.ContainsText()
                    ? System.Windows.Clipboard.GetText()
                    : string.Empty;
            }
            catch (ExternalException ex)
            {
                Debug.WriteLine($"[Clipboard] Could not read clipboard text: {ex.Message}");
                return null;
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"[Clipboard] Could not read clipboard text: {ex.Message}");
                return null;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

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

        private async System.Threading.Tasks.Task PushLocalizationToFrontendAsync()
        {
            if (WebView?.CoreWebView2 == null) return;

            string languageJson = Newtonsoft.Json.JsonConvert.SerializeObject(Localizer.Language);
            string script =
                "(function() {" +
                $"window.__ATOMIX_I18N = {Localizer.ToJson()};" +
                $"window.__ATOMIX_LANG = {languageJson};" +
                "if (window.Atomix && window.Atomix.I18n) {" +
                "window.Atomix.I18n.strings = window.__ATOMIX_I18N;" +
                "window.Atomix.I18n.lang = window.__ATOMIX_LANG;" +
                "document.documentElement.lang = window.__ATOMIX_LANG;" +
                "window.Atomix.I18n.apply(document);" +
                "}" +
                "})();";

            try
            {
                await WebView.CoreWebView2.ExecuteScriptAsync(script);
                Debug.WriteLine($"[i18n] Applied '{Localizer.Language}' after navigation.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[i18n] Apply after navigation failed: {ex}");
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
                    case "paste_clipboard":
                        string clipboardText = TryReadClipboardText() ?? string.Empty;
                        string clipboardHtml = TryReadClipboardHtml() ?? string.Empty;
                        WebView.CoreWebView2.PostWebMessageAsJson(Newtonsoft.Json.JsonConvert.SerializeObject(new
                        {
                            action = "clipboard_text",
                            text = clipboardText,
                            html = clipboardHtml
                        }));
                        break;

                    case "GET_SUB_CONTEXT":
                        string subId = request["payload"]?["id"]?.ToString();
                        if (!string.IsNullOrEmpty(subId))
                        {
                            lock (_uiHandler.UiCommandQueue)
                            {
                                // Складываем в UI-очередь и будим UI ExternalEvent
                                _uiHandler.UiCommandQueue.Enqueue(("GET_SUB_CONTEXT", subId!, string.Empty));
                            }
                            _uiExEvent.Raise(); // Будим выделенный поток Revit для UI
                        }
                        break;

                    case "CREATE_CHIP":
                        string parameterId = request["payload"]?["id"]?.ToString();
                        string chipRequestId = request["payload"]?["requestId"]?.ToString();
                        if (!string.IsNullOrEmpty(parameterId) && !string.IsNullOrEmpty(chipRequestId))
                        {
                            lock (_uiHandler.UiCommandQueue)
                            {
                                _uiHandler.UiCommandQueue.Enqueue(("CREATE_CHIP", parameterId!, chipRequestId!));
                            }
                            _uiExEvent.Raise();
                        }
                        break;

                    case "update_chip":
                        // Пользователь подтвердил значение в поле ввода: в ИИ уйдёт только оно.
                        string updateChipId = request["payload"]?["id"]?.ToString();
                        if (!string.IsNullOrEmpty(updateChipId))
                        {
                            string confirmedValue = request["payload"]?["value"]?.ToString() ?? string.Empty;
                            if (ChipStorage.Shared.TryGetEntry(updateChipId!, out var chipEntry) && chipEntry != null)
                            {
                                var chipData = chipEntry["data"] as Newtonsoft.Json.Linq.JObject
                                               ?? new Newtonsoft.Json.Linq.JObject();
                                string paramName = chipData["parameter_Name"]?.ToString() ?? string.Empty;
                                string label = string.IsNullOrEmpty(confirmedValue)
                                    ? $"#{paramName}"
                                    : $"#{paramName}: {confirmedValue}";

                                chipData["parameter_Value"] = confirmedValue;
                                chipData["parameter_Unit_Label"] = string.Empty;

                                // Оставляем единственное значение: исходное не сохраняем.
                                string storageType = chipData["parameter_Storage_Type"]?.ToString() ?? string.Empty;
                                if (string.Equals(storageType, "String", StringComparison.OrdinalIgnoreCase))
                                {
                                    chipData["value"] = confirmedValue;
                                }
                                else
                                {
                                    chipData.Remove("value");
                                    chipData.Remove("valueUnits");
                                    chipData.Remove("displayUnitId");
                                }

                                var updatedEntry = new Newtonsoft.Json.Linq.JObject
                                {
                                    ["id"] = updateChipId,
                                    ["label"] = label,
                                    ["data"] = chipData
                                };
                                ChipStorage.Shared.SetOverride(updateChipId!, updatedEntry);
                            }
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
                            if (string.IsNullOrWhiteSpace(prompt))
                            {
                                ChipStorage.Shared.DiscardPending();
                                break;
                            }

                            Newtonsoft.Json.Linq.JArray chips;
                            try
                            {
                                chips = ChipStorage.Shared.CommitAndCreateSnapshot(payload["chipIds"] as Newtonsoft.Json.Linq.JArray);
                            }
                            catch (ArgumentException ex)
                            {
                                ChipStorage.Shared.DiscardPending();
                                WebView.CoreWebView2.PostWebMessageAsJson(Newtonsoft.Json.JsonConvert.SerializeObject(new
                                {
                                    action = "ui_log",
                                    role = "ai",
                                    content = ex.Message,
                                    turn = payload["turn"]?.ToObject<int>() ?? 0
                                }));
                                break;
                            }

                            // 3. ПЕРЕДАЧА ИИ: Отправляем очищенный текст промпта асинхронному MCP-клиенту
                            {
                                System.Diagnostics.Debug.WriteLine($"[UI Debug] Prompt sent to AI: {prompt}");

                                int turn = payload["turn"]?.ToObject<int>() ?? 0;
                                int generation = TransactionManager.BindRequest(turn);
                                var pipePayload = Newtonsoft.Json.JsonConvert.SerializeObject(new
                                {
                                    action = "chat_request",
                                    prompt = prompt,
                                    generation = generation,
                                    chips = chips
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
                            int generation = TransactionManager.ActiveRequestGeneration;
                            if (TransactionManager.IsStaleOrCancelled(generation))
                                break;

                            lock (_handler.CommandQueue)
                            {
                                _handler.CommandQueue.Enqueue(new PendingToolCall(toolName, args ?? "{}", generation));
                            }
                            _exEvent.Raise();
                        }
                        break;
                    case "stop":
                        // CurrentHandler — [ThreadStatic] и живёт только внутри Execute() на потоке Revit.
                        // Здесь помечаем поколение и чистим очередь; Rollback делает ExecuteSequence.
                        TransactionManager.RequestCancel();
                        lock (_handler.CommandQueue)
                        {
                            _handler.CommandQueue.Clear();
                        }
                        _mcpHost.BroadcastToClients(Newtonsoft.Json.JsonConvert.SerializeObject(new
                        {
                            action = "abort",
                            generation = TransactionManager.ActiveRequestGeneration
                        }));
                        _exEvent.Raise();
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
