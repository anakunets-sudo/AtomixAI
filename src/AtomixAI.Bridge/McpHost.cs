using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent; // Добавлено для очереди сообщений UI
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.UI;
using AtomixAI.Core;
using AtomixAI.Atomic;

namespace AtomixAI.Bridge
{
    public class McpHost
    {
        private readonly Queue<(string ToolId, string JsonArgs)> _commandQueue;
        private readonly ExternalEvent _externalEvent;
        private bool _isRunning = false;
        public event Action<string> OnMessageReceived;
        private const string PipeName = "AtomixAI_Bridge_Pipe";
        private readonly ToolDispatcher _dispatcher;

        // Очередь сообщений, которые нужно отправить ИЗ интерфейса В Python
        private readonly ConcurrentQueue<string> _uiToPythonQueue = new ConcurrentQueue<string>();

        public McpHost(Queue<(string, string)> queue, ExternalEvent exEvent, ToolDispatcher dispatcher)
        {
            _commandQueue = queue;
            _externalEvent = exEvent;
            _dispatcher = dispatcher;
        }

        // Метод для вызова из WebView2 (OnWebMessageReceived) 
        public void BroadcastToClients(string jsonPayload)
        {
            _uiToPythonQueue.Enqueue(jsonPayload);
        }

        public async Task ListenAsync()
        {
            _isRunning = true;
            var encoding = new UTF8Encoding(false);

            while (_isRunning)
            {
                NamedPipeServerStream pipeServer = null;
                try
                {
                    PipeSecurity pipeSa = new PipeSecurity();
                    pipeSa.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                        PipeAccessRights.ReadWrite,
                        AccessControlType.Allow));

#if NETFRAMEWORK
                    pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                        1024,
                        1024,
                        pipeSa);
#else
                    pipeServer = NamedPipeServerStreamAcl.Create(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                        1024,
                        1024,
                        pipeSa);
#endif
                    await pipeServer.WaitForConnectionAsync();

                    using (var reader = new StreamReader(pipeServer, encoding))
                    using (var writer = new StreamWriter(pipeServer, encoding) { AutoFlush = true })
                    {
                        while (pipeServer.IsConnected && !reader.EndOfStream && _isRunning)
                        {
                            // 1. Читаем запрос от Python (например, опрос или вызов функции)
                            string requestRaw = await reader.ReadLineAsync();
                            //System.Diagnostics.Debug.WriteLine($"[PIPE RECEIVED]: {requestRaw}");

                            if (string.IsNullOrEmpty(requestRaw))
                                break;

                            // 2. Обрабатываем входящий запрос
                            string response = ProcessRequest(requestRaw);

                            // 3. ПРОВЕРКА UI ОЧЕРЕДИ:
                            // Если в очереди есть сообщения из чата WebView2, упаковываем их в ответ
                            if (_uiToPythonQueue.TryDequeue(out string uiMessage))
                            {
                                try
                                {
                                    // Используем анонимные типы аккуратно
                                    var wrappedResponse = new
                                    {
                                        result = requestRaw.StartsWith("{") ? JObject.Parse(response) : (object)response,
                                        ui_event = JObject.Parse(uiMessage)
                                    };
                                    response = JsonConvert.SerializeObject(wrappedResponse);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[JSON Wrap Error]: {ex.Message}");
                                }
                            }

                            // 4. Отправляем ответ
                            byte[] data = encoding.GetBytes(response + "\n");
                            await pipeServer.WriteAsync(data, 0, data.Length);
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Pipe Error]: {ex.Message}");
                        await Task.Delay(500);
                    }
                }
                finally
                {
                    if (pipeServer != null)
                    {
                        if (pipeServer.IsConnected)
                            pipeServer.Disconnect();
                        pipeServer.Dispose();
                    }
                }

                await Task.Delay(100);
            }
        }

        // Внутри McpHost.cs добавим метод для отправки результата выполнения команды
        public void SendToolResult(AtomicResult result, string toolId)
        {
            var response = new
            {
                action = "tool_execution_result",
                tool = toolId,
                success = result.Success,
                message = result.Message,
                data = result.Data // Например, количество найденных элементов
            };

            // Кладём в очередь для отправки в Python при следующем опросе (poll)
            BroadcastToClients(JsonConvert.SerializeObject(response));
        }
        private string ProcessRequest(string json)
        {
            try
            {
                var request = JObject.Parse(json);
                string action = request["action"]?.ToString();

                // ИСПРАВЛЕНО: Теперь всегда возвращаем JSON объект для Python
                if (action == "get_manual")
                    return JsonConvert.SerializeObject(new { manual = Registry.GetAiManual() });

                if (action == "get_context_state")
                    return JsonConvert.SerializeObject(new { aliases = Registry.GetActiveContentStateAliases() });

                // НОВЫЙ КЕЙС: Ответ от ИИ для интерфейса
                if (action == "ui_log")
                {
                    // Пробрасываем JSON целиком (там уже есть role и content)
                    OnMessageReceived?.Invoke(json);
                    return JsonConvert.SerializeObject(new { status = "delivered_to_ui" });
                }

                if (action == "list")
                {
                    return Registry.GetToolsJson();
                }
                else if (action == "call")
                {
                    string toolName = request["name"]?.ToString();
                    string args = request["arguments"]?.ToString();

                    if (!string.IsNullOrEmpty(toolName))
                    {
                        lock (_commandQueue)
                        {
                            _commandQueue.Enqueue((toolName, args));
                        }
                        _externalEvent.Raise();
                        return JsonConvert.SerializeObject(new { status = "queued", tool = toolName });
                    }
                }

                return JsonConvert.SerializeObject(new { status = "ok" });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = $"Processing Error: {ex.Message}" });
            }
        }

        public void Stop() => _isRunning = false;
    }
}