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
    /// Service for capturing screen content - uses Windows API for forced capture
    /// </summary>
    public class ScreenCaptureService
    {
        // Windows API for forced screen capture (bypasses app blocking)
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

        public event Action<string>? OnFrameCaptured; // Base64 encoded JPEG image

        public void Start()
        {
            if (isRunning) return;

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
                // Get primary screen bounds
                var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

                // Calculate scaled dimensions for faster transmission
                int scaledWidth = (int)(bounds.Width * scaleFactor);
                int scaledHeight = (int)(bounds.Height * scaleFactor);

                // Capture using BitBlt API (bypasses app blocking - FORCED CAPTURE)
                IntPtr desktopHandle = GetDesktopWindow();
                IntPtr desktopDC = GetWindowDC(desktopHandle);
                IntPtr memoryDC = CreateCompatibleDC(desktopDC);
                IntPtr bitmapHandle = CreateCompatibleBitmap(desktopDC, bounds.Width, bounds.Height);
                IntPtr oldBitmap = SelectObject(memoryDC, bitmapHandle);
                
                // Force capture entire screen (no app can block this)
                BitBlt(memoryDC, 0, 0, bounds.Width, bounds.Height, desktopDC, bounds.X, bounds.Y, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
                
                // Convert to managed Bitmap
                Bitmap fullBitmap = Image.FromHbitmap(bitmapHandle);
                
                // Cleanup Windows handles
                SelectObject(memoryDC, oldBitmap);
                DeleteObject(bitmapHandle);
                DeleteDC(memoryDC);
                ReleaseDC(desktopHandle, desktopDC);

                // Scale down for faster transmission
                using var scaledBitmap = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
                using (var scaledGraphics = Graphics.FromImage(scaledBitmap))
                {
                    scaledGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low; // Fast scaling
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
