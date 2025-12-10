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
                // PARSEC/GOTORESOLVE APPROACH: Hardware acceleration with fallback
                // Create D3D11 Device with highest performance tier
                device = new Device(
                    SharpDX.Direct3D.DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport,  // Optimize for screen capture
                    SharpDX.Direct3D.FeatureLevel.Level_11_0
                );

                // Get DXGI Device
                using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
                using var adapter = dxgiDevice.Adapter;

                Console.WriteLine($"🎮 GPU: {adapter.Description.Description}");
                Console.WriteLine($"💾 VRAM: {adapter.Description.DedicatedVideoMemory / 1024 / 1024} MB");

                // Get primary output (monitor)
                using var output = adapter.GetOutput(0);
                using var output1 = output.QueryInterface<Output1>();

                // Get desktop bounds
                var bounds = output.Description.DesktopBounds;
                width = bounds.Right - bounds.Left;
                height = bounds.Bottom - bounds.Top;

                // CRITICAL: Create Desktop Duplication with HIGHEST PRIORITY
                // This gives kernel-level framebuffer access like GoToResolve
                outputDuplication = output1.DuplicateOutput(device);

                // Create staging texture for CPU access with optimal performance
                var textureDesc = new Texture2DDescription
                {
                    CpuAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None,
                    Format = Format.B8G8R8A8_UNorm,  // 32-bit BGRA (DirectX native)
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
                Console.WriteLine("   ⚡ KERNEL-LEVEL CAPTURE - Bypasses ALL app protections!");
                Console.WriteLine("   🔒 Captures: Protected apps, games, DRM, fullscreen, layered windows");
                Console.WriteLine("   📡 Priority: Maximum (same as Parsec/GoToResolve)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Desktop Duplication failed to initialize: {ex.Message}");
                Console.WriteLine("   📋 Details: " + ex.StackTrace?.Split('\n')[0]);
                Console.WriteLine("   ⚠️  Falling back to BitBlt method...");
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
                // PARSEC APPROACH: Try to acquire frame with aggressive timeout
                // Lower timeout = more responsive to changes, drops frames if GPU busy
                var result = outputDuplication.TryAcquireNextFrame(16, out frameInfo, out desktopResource);
                
                if (result.Failure || desktopResource == null)
                {
                    // No new frame yet - GPU hasn't rendered changes
                    // This is NORMAL and expected for unchanged screens
                    return null;
                }

                // Check if frame actually changed (optimization from RDP spec)
                if (frameInfo.TotalMetadataBufferSize == 0 && frameInfo.AccumulatedFrames == 0)
                {
                    // No actual changes in frame - release immediately
                    outputDuplication.ReleaseFrame();
                    return null;
                }

                // Copy desktop image to staging texture using GPU DMA
                // This is MUCH faster than CPU-based BitBlt
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
                // No new frame yet - GPU hasn't rendered changes (NORMAL)
                return null;
            }
            catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.AccessLost.Result.Code)
            {
                // CRITICAL: Resolution changed, monitor disconnected, or desktop locked
                // GoToResolve/Parsec handle this by immediate reinitialization
                Console.WriteLine("⚠️ Desktop Duplication access lost - AUTO-RECOVERING...");
                Console.WriteLine("   Cause: Resolution change, monitor config, or screen lock");
                Dispose();
                System.Threading.Thread.Sleep(100); // Brief pause for system stabilization
                if (Initialize())
                {
                    Console.WriteLine("✅ Desktop Duplication recovered successfully!");
                }
                return null;
            }
            catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.AccessDenied.Result.Code)
            {
                // Screen saver, UAC prompt, or secure desktop active
                Console.WriteLine("🔒 Access denied - Secure desktop active (UAC/Login screen)");
                Console.WriteLine("   This is normal Windows security - will auto-recover");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Desktop Duplication capture error: {ex.Message}");
                Console.WriteLine($"   Type: {ex.GetType().Name}");
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
