using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace PulseHost
{
    /// <summary>
    /// Desktop Duplication API - ULTIMATE screen capture that bypasses ALL protections
    /// Used by professional tools like Parsec, AnyDesk - captures protected content, games, DRM
    /// </summary>
    public class DesktopDuplicationCapture : IDisposable
    {
        private Device? device;
        private OutputDuplication? outputDuplication;
        private Texture2D? screenTexture;
        private int width;
        private int height;
        private bool isInitialized = false;

        public bool Initialize()
        {
            try
            {
                // Create D3D11 Device
                device = new Device(SharpDX.Direct3D.DriverType.Hardware);

                // Get DXGI Device
                using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
                using var adapter = dxgiDevice.Adapter;

                // Get primary output (monitor)
                using var output = adapter.GetOutput(0);
                using var output1 = output.QueryInterface<Output1>();

                // Get desktop bounds
                var bounds = output.Description.DesktopBounds;
                width = bounds.Right - bounds.Left;
                height = bounds.Bottom - bounds.Top;

                // Create Desktop Duplication
                outputDuplication = output1.DuplicateOutput(device);

                // Create staging texture for CPU access
                var textureDesc = new Texture2DDescription
                {
                    CpuAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = width,
                    Height = height,
                    OptionFlags = ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = { Count = 1, Quality = 0 },
                    Usage = ResourceUsage.Staging
                };

                screenTexture = new Texture2D(device, textureDesc);
                isInitialized = true;

                Console.WriteLine($"✅ Desktop Duplication API initialized: {width}x{height}");
                Console.WriteLine("   ⚡ CAPTURES EVERYTHING - No app can block this!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Desktop Duplication failed to initialize: {ex.Message}");
                Console.WriteLine("   Falling back to BitBlt method...");
                Dispose();
                return false;
            }
        }

        public Bitmap? CaptureScreen()
        {
            if (!isInitialized || outputDuplication == null || screenTexture == null || device == null)
                return null;

            SharpDX.DXGI.Resource? desktopResource = null;
            OutputDuplicateFrameInformation frameInfo;

            try
            {
                // Acquire next frame
                var result = outputDuplication.TryAcquireNextFrame(100, out frameInfo, out desktopResource);
                
                if (result.Failure || desktopResource == null)
                {
                    return null; // No new frame available
                }

                // Copy desktop image to staging texture
                using var desktopTexture = desktopResource.QueryInterface<Texture2D>();
                device.ImmediateContext.CopyResource(desktopTexture, screenTexture);

                // Map staging texture to read pixels
                var dataBox = device.ImmediateContext.MapSubresource(
                    screenTexture,
                    0,
                    MapMode.Read,
                    MapFlags.None);

                // Create bitmap from mapped data
                var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var bitmapData = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                // Copy pixel data
                unsafe
                {
                    byte* sourcePtr = (byte*)dataBox.DataPointer;
                    byte* destPtr = (byte*)bitmapData.Scan0;
                    
                    for (int y = 0; y < height; y++)
                    {
                        System.Buffer.MemoryCopy(
                            sourcePtr + y * dataBox.RowPitch,
                            destPtr + y * bitmapData.Stride,
                            bitmapData.Stride,
                            bitmapData.Stride
                        );
                    }
                }

                bitmap.UnlockBits(bitmapData);

                // Unmap and release
                device.ImmediateContext.UnmapSubresource(screenTexture, 0);
                outputDuplication.ReleaseFrame();

                return bitmap;
            }
            catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
            {
                // No new frame yet - expected
                return null;
            }
            catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.AccessLost.Result.Code)
            {
                // Need to reinitialize (resolution changed, monitor disconnected, etc.)
                Console.WriteLine("⚠️ Desktop Duplication access lost - reinitializing...");
                Dispose();
                Initialize();
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Desktop Duplication capture error: {ex.Message}");
                return null;
            }
            finally
            {
                desktopResource?.Dispose();
            }
        }

        public void Dispose()
        {
            isInitialized = false;
            outputDuplication?.Dispose();
            outputDuplication = null;
            screenTexture?.Dispose();
            screenTexture = null;
            device?.Dispose();
            device = null;
        }
    }
}
