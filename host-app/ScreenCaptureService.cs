using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace PulseHost
{
    /// <summary>
    /// Service for capturing screen content
    /// Uses Desktop Duplication API (ULTIMATE - captures EVERYTHING including protected apps)
    /// Falls back to BitBlt if Desktop Duplication unavailable
    /// </summary>
    public class ScreenCaptureService
    {
        // Windows API for BitBlt fallback
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        
        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSource, int xSrc, int ySrc, CopyPixelOperation rop);
        
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
        
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
        
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        private bool isRunning = false;
        private Task? captureTask;
        private CancellationTokenSource? cts;
        private int fps = 25; // Target frames per second - increased for responsiveness
        private int quality = 35; // JPEG quality (1-100) - VERY LOW for minimum latency
        private double scaleFactor = 0.6; // Scale down to 60% to reduce bandwidth

        private DesktopDuplicationCapture? desktopDuplication;
        private bool useDesktopDuplication = true;

        public event Action<string>? OnFrameCaptured; // Base64 encoded JPEG image

        public void Start()
        {
            if (isRunning) return;

            // Try to initialize Desktop Duplication API (captures EVERYTHING)
            Console.WriteLine("🚀 Initializing screen capture...");
            desktopDuplication = new DesktopDuplicationCapture();
            useDesktopDuplication = desktopDuplication.Initialize();
            
            if (!useDesktopDuplication)
            {
                Console.WriteLine("⚠️ Using BitBlt fallback (some apps may be blocked)");
            }

            isRunning = true;
            cts = new CancellationTokenSource();
            captureTask = Task.Run(() => CaptureLoop(cts.Token));
        }

        public void Stop()
        {
            if (!isRunning) return;

            isRunning = false;
            cts?.Cancel();
            captureTask?.Wait(1000);
            cts?.Dispose();
            
            desktopDuplication?.Dispose();
            desktopDuplication = null;
        }

        private async Task CaptureLoop(CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromMilliseconds(1000.0 / fps);

            while (isRunning && !cancellationToken.IsCancellationRequested)
            {
                var startTime = DateTime.Now;

                try
                {
                    var imageData = CaptureScreen();
                    if (imageData != null)
                    {
                        OnFrameCaptured?.Invoke(imageData);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Screen capture error: {ex.Message}");
                }

                var elapsed = DateTime.Now - startTime;
                var delay = interval - elapsed;
                
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        private string? CaptureScreen()
        {
            try
            {
                Bitmap? fullBitmap = null;

                // Try Desktop Duplication first (ULTIMATE capture - no blocking)
                if (useDesktopDuplication && desktopDuplication != null)
                {
                    fullBitmap = desktopDuplication.CaptureScreen();
                    
                    // If Desktop Duplication fails, fall back to BitBlt
                    if (fullBitmap == null)
                    {
                        useDesktopDuplication = false;
                        Console.WriteLine("⚠️ Desktop Duplication failed, switching to BitBlt");
                    }
                }

                // Fallback to BitBlt if Desktop Duplication unavailable or failed
                if (fullBitmap == null)
                {
                    var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
                    
                    IntPtr desktopHandle = GetDesktopWindow();
                    IntPtr desktopDC = GetWindowDC(desktopHandle);
                    IntPtr memoryDC = CreateCompatibleDC(desktopDC);
                    IntPtr bitmapHandle = CreateCompatibleBitmap(desktopDC, bounds.Width, bounds.Height);
                    IntPtr oldBitmap = SelectObject(memoryDC, bitmapHandle);
                    
                    BitBlt(memoryDC, 0, 0, bounds.Width, bounds.Height, desktopDC, bounds.X, bounds.Y, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
                    
                    fullBitmap = Image.FromHbitmap(bitmapHandle);
                    
                    SelectObject(memoryDC, oldBitmap);
                    DeleteObject(bitmapHandle);
                    DeleteDC(memoryDC);
                    ReleaseDC(desktopHandle, desktopDC);
                }

                if (fullBitmap == null)
                    return null;

                // Calculate scaled dimensions
                int scaledWidth = (int)(fullBitmap.Width * scaleFactor);
                int scaledHeight = (int)(fullBitmap.Height * scaleFactor);

                // Scale down for faster transmission
                using (fullBitmap)
                using (var scaledBitmap = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb))
                {
                    using (var scaledGraphics = Graphics.FromImage(scaledBitmap))
                    {
                        scaledGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                        scaledGraphics.DrawImage(fullBitmap, 0, 0, scaledWidth, scaledHeight);
                    }

                    // Compress to JPEG
                    using var memoryStream = new MemoryStream();
                    var encoderParameters = new EncoderParameters(1);
                    encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    
                    var jpegCodec = GetEncoder(ImageFormat.Jpeg);
                    if (jpegCodec != null)
                    {
                        scaledBitmap.Save(memoryStream, jpegCodec, encoderParameters);
                    }
                    else
                    {
                        scaledBitmap.Save(memoryStream, ImageFormat.Jpeg);
                    }

                    // Convert to base64
                    var bytes = memoryStream.ToArray();
                    return Convert.ToBase64String(bytes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to capture screen: {ex.Message}");
                return null;
            }
        }

        private static ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        public void SetFPS(int newFps)
        {
            fps = Math.Clamp(newFps, 1, 60);
        }

        public void SetQuality(int newQuality)
        {
            quality = Math.Clamp(newQuality, 1, 100);
        }
    }
}
