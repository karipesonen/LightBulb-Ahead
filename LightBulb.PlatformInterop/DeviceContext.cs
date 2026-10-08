using System.Diagnostics;
using System.Runtime.InteropServices;
using LightBulb.PlatformInterop.Internal;

namespace LightBulb.PlatformInterop;

public partial class DeviceContext(nint handle) : NativeResource(handle)
{
    private int _gammaChannelOffset;

    private void SetGammaRamp(GammaRamp ramp)
    {
        if (!NativeMethods.SetDeviceGammaRamp(Handle, ref ramp))
        {
            Debug.WriteLine(
                $"Failed to set gamma ramp on device context #{Handle}). "
                    + $"Error {Marshal.GetLastWin32Error()}."
            );
        }
    }

    public void SetGamma(double redMultiplier, double greenMultiplier, double blueMultiplier)
    {
        // Some drivers will ignore requests to change gamma if the specified ramp is the same as last time,
        // even if the actual gamma has been changed in-between (for example, by screen going to sleep).
        // Vary nonzero channels while preserving fully suppressed channels.
        _gammaChannelOffset = ++_gammaChannelOffset % 5;
        SetGammaRamp(
            GammaRamp.Create(redMultiplier, greenMultiplier, blueMultiplier, _gammaChannelOffset)
        );
    }

    public void ResetGamma() => SetGamma(1, 1, 1);

    protected override void Dispose(bool disposing)
    {
        // Don't reset gamma during dispose because this method is also called whenever
        // the device context gets invalidated.
        // Resetting gamma in such cases will cause unwanted flickering.
        // https://github.com/Tyrrrz/LightBulb/issues/206
        if (!NativeMethods.DeleteDC(Handle))
        {
            Debug.WriteLine(
                $"Failed to dispose device context #{Handle}. "
                    + $"Error {Marshal.GetLastWin32Error()}."
            );
        }
    }
}

public partial class DeviceContext
{
    public static DeviceContext? TryCreate(string deviceName)
    {
        var handle = NativeMethods.CreateDC(deviceName, deviceName, null, 0);
        if (handle == 0)
        {
            Debug.WriteLine(
                $"Failed to retrieve device context for '{deviceName}'. "
                    + $"Error {Marshal.GetLastWin32Error()}."
            );
            return null;
        }

        return new DeviceContext(handle);
    }
}
