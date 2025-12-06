using System;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace PulseHost
{
    /// <summary>
    /// Service for simulating mouse and keyboard input
    /// </summary>
    public class InputSimulator
    {
        // Windows API constants
        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const int MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const int MOUSEEVENTF_WHEEL = 0x0800;
        private const int MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const int KEYEVENTF_KEYDOWN = 0x0000;
        private const int KEYEVENTF_KEYUP = 0x0002;

        // Windows API imports
        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        private int screenWidth;
        private int screenHeight;

        public InputSimulator()
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            screenWidth = bounds.Width;
            screenHeight = bounds.Height;
        }

        public void ProcessInput(string inputType, JObject data)
        {
            try
            {
                switch (inputType)
                {
                    case "mouseMove":
                        HandleMouseMove(data);
                        break;
                    case "mouseDown":
                        HandleMouseDown(data);
                        break;
                    case "mouseUp":
                        HandleMouseUp(data);
                        break;
                    case "mouseWheel":
                        HandleMouseWheel(data);
                        break;
                    case "keyDown":
                        HandleKeyDown(data);
                        break;
                    case "keyUp":
                        HandleKeyUp(data);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Input processing error: {ex.Message}");
            }
        }

        private void HandleMouseMove(JObject data)
        {
            var xNorm = data["xNorm"]?.Value<double>() ?? 0;
            var yNorm = data["yNorm"]?.Value<double>() ?? 0;

            // Convert normalized coordinates to absolute screen coordinates
            var x = (int)(xNorm * 65535);
            var y = (int)(yNorm * 65535);

            mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, x, y, 0, 0);
        }

        private void HandleMouseDown(JObject data)
        {
            var xNorm = data["xNorm"]?.Value<double>() ?? 0;
            var yNorm = data["yNorm"]?.Value<double>() ?? 0;
            var button = data["button"]?.Value<int>() ?? 0;

            var x = (int)(xNorm * 65535);
            var y = (int)(yNorm * 65535);

            int flags = MOUSEEVENTF_ABSOLUTE;
            flags |= button switch
            {
                0 => MOUSEEVENTF_LEFTDOWN,
                1 => MOUSEEVENTF_MIDDLEDOWN,
                2 => MOUSEEVENTF_RIGHTDOWN,
                _ => MOUSEEVENTF_LEFTDOWN
            };

            mouse_event(flags, x, y, 0, 0);
        }

        private void HandleMouseUp(JObject data)
        {
            var xNorm = data["xNorm"]?.Value<double>() ?? 0;
            var yNorm = data["yNorm"]?.Value<double>() ?? 0;
            var button = data["button"]?.Value<int>() ?? 0;

            var x = (int)(xNorm * 65535);
            var y = (int)(yNorm * 65535);

            int flags = MOUSEEVENTF_ABSOLUTE;
            flags |= button switch
            {
                0 => MOUSEEVENTF_LEFTUP,
                1 => MOUSEEVENTF_MIDDLEUP,
                2 => MOUSEEVENTF_RIGHTUP,
                _ => MOUSEEVENTF_LEFTUP
            };

            mouse_event(flags, x, y, 0, 0);
        }

        private void HandleMouseWheel(JObject data)
        {
            var deltaY = data["deltaY"]?.Value<int>() ?? 0;
            
            // Windows uses 120 units per "notch" of the wheel
            var wheelDelta = -deltaY; // Invert for natural scrolling

            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, wheelDelta, 0);
        }

        private void HandleKeyDown(JObject data)
        {
            var keyCode = data["keyCode"]?.Value<int>() ?? 0;
            var key = data["key"]?.Value<string>();

            byte vk = GetVirtualKeyCode(keyCode, key);
            if (vk != 0)
            {
                keybd_event(vk, 0, KEYEVENTF_KEYDOWN, 0);
            }
        }

        private void HandleKeyUp(JObject data)
        {
            var keyCode = data["keyCode"]?.Value<int>() ?? 0;
            var key = data["key"]?.Value<string>();

            byte vk = GetVirtualKeyCode(keyCode, key);
            if (vk != 0)
            {
                keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
            }
        }

        private byte GetVirtualKeyCode(int keyCode, string? key)
        {
            // Map common JavaScript keyCodes to Windows Virtual Key codes
            // This is a simplified mapping; expand as needed
            return keyCode switch
            {
                8 => 0x08,   // Backspace
                9 => 0x09,   // Tab
                13 => 0x0D,  // Enter
                16 => 0x10,  // Shift
                17 => 0x11,  // Ctrl
                18 => 0x12,  // Alt
                27 => 0x1B,  // Escape
                32 => 0x20,  // Space
                33 => 0x21,  // Page Up
                34 => 0x22,  // Page Down
                35 => 0x23,  // End
                36 => 0x24,  // Home
                37 => 0x25,  // Left Arrow
                38 => 0x26,  // Up Arrow
                39 => 0x27,  // Right Arrow
                40 => 0x28,  // Down Arrow
                45 => 0x2D,  // Insert
                46 => 0x2E,  // Delete
                
                // Numbers 0-9
                >= 48 and <= 57 => (byte)keyCode,
                
                // Letters A-Z
                >= 65 and <= 90 => (byte)keyCode,
                
                // Function keys F1-F12
                >= 112 and <= 123 => (byte)keyCode,
                
                _ => 0
            };
        }
    }
}
