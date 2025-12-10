using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PulseHost
{
    public class WebSocketClient
    {
        private readonly string serverUrl;
        private ClientWebSocket? ws;
        private CancellationTokenSource? cts;
        private System.Timers.Timer? keepAliveTimer;
        private DateTime lastMessageTime = DateTime.Now;

        public event Action<string, string>? OnPaired; // deviceId, deviceToken
        public event Action<string, string>? OnSessionRequest; // sessionId, userName
        public event Action<string>? OnSessionEnded; // sessionId
        public event Action<string, string, JObject>? OnInputEvent; // sessionId, inputType, data
        public event Action? OnDisconnected;

        public WebSocketClient(string serverUrl)
        {
            this.serverUrl = serverUrl;
        }

        public async Task Connect()
        {
            ws = new ClientWebSocket();
            
            // Configure WebSocket for long-running connections
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            
            cts = new CancellationTokenSource();

            Console.WriteLine($"🔌 Connecting to {serverUrl}...");
            await ws.ConnectAsync(new Uri(serverUrl), cts.Token);
            Console.WriteLine($"✅ Connected! State: {ws.State}");
            
            // Start keep-alive ping mechanism
            StartKeepAlive();
            
            _ = ReceiveLoop();
        }
        
        private void StartKeepAlive()
        {
            // Send ping every 20 seconds to keep connection alive
            keepAliveTimer = new System.Timers.Timer(20000);
            keepAliveTimer.Elapsed += async (sender, e) =>
            {
                if (ws?.State == WebSocketState.Open)
                {
                    // Check if we've received any message in the last 60 seconds
                    var timeSinceLastMessage = (DateTime.Now - lastMessageTime).TotalSeconds;
                    
                    if (timeSinceLastMessage > 60)
                    {
                        Console.WriteLine($"⚠️ No messages received for {timeSinceLastMessage:F0} seconds - connection may be dead");
                    }
                    
                    try
                    {
                        await Send(new { type = "ping" });
                        Console.WriteLine($"💓 Keep-alive ping sent (last message: {timeSinceLastMessage:F0}s ago)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Keep-alive ping failed: {ex.Message}");
                    }
                }
            };
            keepAliveTimer.Start();
            Console.WriteLine($"💓 Keep-alive timer started (ping every 20s)");
        }

        private async Task ReceiveLoop()
        {
            if (ws == null || cts == null) return;

            var buffer = new byte[1024 * 1024]; // 1MB buffer for large frames
            Console.WriteLine($"🔌 WebSocket receive loop started - State: {ws.State}");

            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine($"❌ WebSocket CLOSED by server");
                        Console.WriteLine($"   Close Status: {result.CloseStatus}");
                        Console.WriteLine($"   Close Description: {result.CloseStatusDescription}");
                        Console.WriteLine($"   Time: {DateTime.Now:HH:mm:ss.fff}");
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        OnDisconnected?.Invoke();
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    lastMessageTime = DateTime.Now;
                    HandleMessage(message);
                }
                
                Console.WriteLine($"⚠️ WebSocket receive loop ended - Final State: {ws.State}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"ℹ️ WebSocket receive loop cancelled (normal shutdown)");
            }
            catch (WebSocketException wsEx)
            {
                Console.WriteLine($"\n❌ WEBSOCKET EXCEPTION:");
                Console.WriteLine($"   Type: WebSocketException");
                Console.WriteLine($"   Error Code: {wsEx.WebSocketErrorCode}");
                Console.WriteLine($"   Native Error Code: {wsEx.NativeErrorCode}");
                Console.WriteLine($"   Message: {wsEx.Message}");
                Console.WriteLine($"   WebSocket State: {ws?.State}");
                Console.WriteLine($"   Time: {DateTime.Now:HH:mm:ss.fff}");
                Console.WriteLine($"   Stack: {wsEx.StackTrace}\n");
                OnDisconnected?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ RECEIVE LOOP EXCEPTION:");
                Console.WriteLine($"   Type: {ex.GetType().Name}");
                Console.WriteLine($"   Message: {ex.Message}");
                Console.WriteLine($"   WebSocket State: {ws?.State}");
                Console.WriteLine($"   Time: {DateTime.Now:HH:mm:ss.fff}");
                Console.WriteLine($"   Stack: {ex.StackTrace}\n");
                OnDisconnected?.Invoke();
            }
        }

        private void HandleMessage(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                var type = json["type"]?.ToString();

                switch (type)
                {
                    case "paired":
                        OnPaired?.Invoke(
                            json["deviceId"]?.ToString() ?? "",
                            json["deviceToken"]?.ToString() ?? ""
                        );
                        break;

                    case "authenticated":
                        // Successfully authenticated
                        break;

                    case "sessionRequest":
                        OnSessionRequest?.Invoke(
                            json["sessionId"]?.ToString() ?? "",
                            json["userName"]?.ToString() ?? ""
                        );
                        break;

                    case "sessionEnded":
                        OnSessionEnded?.Invoke(json["sessionId"]?.ToString() ?? "");
                        break;

                    case "input":
                        OnInputEvent?.Invoke(
                            json["sessionId"]?.ToString() ?? "",
                            json["inputType"]?.ToString() ?? "",
                            json["data"]?.ToObject<JObject>() ?? new JObject()
                        );
                        break;

                    case "error":
                        Console.WriteLine($"Server error: {json["error"]}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse message: {ex.Message}");
            }
        }

        public async Task SendPairingRequest(string pairingCode, string deviceName, string osInfo)
        {
            await Send(new
            {
                type = "pair",
                pairingCode,
                deviceName,
                osInfo
            });
        }

        public async Task SendAuthentication(string deviceToken)
        {
            await Send(new
            {
                type = "authenticate",
                deviceToken
            });
        }

        public void SendSessionResponse(string sessionId, bool accepted)
        {
            _ = Send(new
            {
                type = "sessionResponse",
                sessionId,
                accepted
            });
        }

        public void SendFrame(string sessionId, int frameNumber, string imageData)
        {
            _ = Send(new
            {
                type = "frame",
                sessionId,
                frameNumber,
                imageData
            });
        }

        public void SendEndSession(string sessionId)
        {
            _ = Send(new
            {
                type = "endSession",
                sessionId
            });
        }

        public void SendLog(string level, string message, string? deviceId = null)
        {
            _ = Send(new
            {
                type = "log",
                level,
                message,
                deviceId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            });
        }

        private async Task Send(object data)
        {
            if (ws == null || cts == null)
            {
                Console.WriteLine($"⚠️ Cannot send: WebSocket is null");
                return;
            }
            
            if (ws.State != WebSocketState.Open)
            {
                Console.WriteLine($"⚠️ Cannot send: WebSocket state is {ws.State}");
                return;
            }

            try
            {
                var json = JsonConvert.SerializeObject(data);
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
            }
            catch (WebSocketException wsEx)
            {
                Console.WriteLine($"\n❌ SEND FAILED - WebSocketException:");
                Console.WriteLine($"   Error Code: {wsEx.WebSocketErrorCode}");
                Console.WriteLine($"   Message: {wsEx.Message}");
                Console.WriteLine($"   State: {ws.State}");
                Console.WriteLine($"   Time: {DateTime.Now:HH:mm:ss.fff}\n");
                
                // Trigger disconnection handler
                OnDisconnected?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ SEND FAILED - {ex.GetType().Name}:");
                Console.WriteLine($"   Message: {ex.Message}");
                Console.WriteLine($"   State: {ws?.State}");
                Console.WriteLine($"   Time: {DateTime.Now:HH:mm:ss.fff}\n");
            }
        }

        public void Disconnect()
        {
            try
            {
                Console.WriteLine($"🔌 Disconnecting WebSocket...");
                keepAliveTimer?.Stop();
                keepAliveTimer?.Dispose();
                keepAliveTimer = null;
                
                cts?.Cancel();
                ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(1000);
                ws?.Dispose();
                Console.WriteLine($"✅ Disconnected cleanly");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Disconnect exception: {ex.Message}");
            }
        }
    }
}
