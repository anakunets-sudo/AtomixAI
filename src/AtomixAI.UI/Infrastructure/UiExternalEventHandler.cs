using Autodesk.Revit.UI;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace AtomixAI.UI.Infrastructure
{
    public class UiExternalEventHandler : IExternalEventHandler
    {
        // Своя изолированная очередь только для UI-запросов
        public readonly Queue<(string Action, string ItemId, string RequestId)> UiCommandQueue = new Queue<(string, string, string)>();

        private WebView2 _webView;
        private Dispatcher _uiDispatcher;

        // Инициализируем мост связи с WebView2
        public void InitializeBridge(WebView2 webView, Dispatcher dispatcher)
        {
            _webView = webView;
            _uiDispatcher = dispatcher;
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                (string Action, string ItemId, string RequestId) task;
                lock (UiCommandQueue)
                {
                    if (UiCommandQueue.Count == 0) break;
                    task = UiCommandQueue.Dequeue();
                }

                // Вызываем наш UI-диспетчер
                string jsonResponse = WebViewUiCommandDispatcher.Dispatch(task.Action, task.ItemId, task.RequestId, app);

                if (!string.IsNullOrEmpty(jsonResponse) && _webView?.CoreWebView2 != null)
                {
                    // Мгновенно возвращаем данные в JS в потоке интерфейса
                    _uiDispatcher.Invoke(() =>
                    {
                        _webView.CoreWebView2.PostWebMessageAsString(jsonResponse);
                    });
                }
            }
        }

        public string GetName() => "AtomixAI_UI_ExternalEvent";
    }
}
