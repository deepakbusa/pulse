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
    /// </summary>
    public class ScreenCaptureService
    {
        private bool isRunning = false;
        private Task? captureTask;
        private CancellationTokenSource? cts;
        private int fps = 15; // Target frames per second
        private int quality = 80; // JPEG quality (1-100)

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

                // Create bitmap
                using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);

                // Capture screen
                graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

                // Compress to JPEG
                using var memoryStream = new MemoryStream();
                var encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                
                var jpegCodec = GetEncoder(ImageFormat.Jpeg);
                if (jpegCodec != null)
                {
                    bitmap.Save(memoryStream, jpegCodec, encoderParameters);
                }
                else
                {
                    bitmap.Save(memoryStream, ImageFormat.Jpeg);
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
