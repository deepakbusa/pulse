using System;

namespace PulseHost
{
    /// <summary>
    /// Helper class to send logs to backend server
    /// </summary>
    public static class LogHelper
    {
        private static WebSocketClient? wsClient;

        public static void Initialize(WebSocketClient client)
        {
            wsClient = client;
        }

        public static void LogInfo(string message, bool sendToBackend = true)
        {
            Console.WriteLine($"ℹ️ {message}");
            if (sendToBackend && wsClient != null)
            {
                wsClient.SendLog("info", message);
            }
        }

        public static void LogWarning(string message, bool sendToBackend = true)
        {
            Console.WriteLine($"⚠️ {message}");
            if (sendToBackend && wsClient != null)
            {
                wsClient.SendLog("warn", message);
            }
        }

        public static void LogError(string message, bool sendToBackend = true)
        {
            Console.WriteLine($"❌ {message}");
            if (sendToBackend && wsClient != null)
            {
                wsClient.SendLog("error", message);
            }
        }

        public static void LogSuccess(string message, bool sendToBackend = true)
        {
            Console.WriteLine($"✅ {message}");
            if (sendToBackend && wsClient != null)
            {
                wsClient.SendLog("success", message);
            }
        }

        public static void LogDebug(string message, bool sendToBackend = false)
        {
            Console.WriteLine($"🔍 {message}");
            if (sendToBackend && wsClient != null)
            {
                wsClient.SendLog("debug", message);
            }
        }
    }
}
