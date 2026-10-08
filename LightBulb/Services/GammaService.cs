using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LightBulb.Core;
using LightBulb.PlatformInterop;
using PowerKit;
using PowerKit.Extensions;

namespace LightBulb.Services;

public partial class GammaService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly IDisposable _eventSubscription;

    private bool _isUpdatingGamma;

    private IReadOnlyList<DeviceContext> _deviceContexts = [];
    private bool _areDeviceContextsValid;
    private DateTimeOffset _lastGammaInvalidationTimestamp = DateTimeOffset.MinValue;

    private ColorConfiguration? _lastConfiguration;
    private DateTimeOffset _lastUpdateTimestamp = DateTimeOffset.MinValue;

    public GammaService(SettingsService settingsService)
    {
        _settingsService = settingsService;

        // Listen to all system events that may indicate that the device context or gamma was changed from the outside
        _eventSubscription = Disposable.Merge(
            // https://github.com/Tyrrrz/LightBulb/issues/223
            SystemHook.TryRegister(SystemHook.Ids.ForegroundWindowChanged, InvalidateGamma)
                ?? Disposable.Null,
            PowerSettingNotification.TryRegister(
                PowerSettingNotification.Ids.ConsoleDisplayStateChanged,
                InvalidateGamma
            ) ?? Disposable.Null,
            PowerSettingNotification.TryRegister(
                PowerSettingNotification.Ids.PowerSavingStatusChanged,
                InvalidateGamma
            ) ?? Disposable.Null,
            PowerSettingNotification.TryRegister(
                PowerSettingNotification.Ids.SessionDisplayStatusChanged,
                InvalidateGamma
            ) ?? Disposable.Null,
            PowerSettingNotification.TryRegister(
                PowerSettingNotification.Ids.MonitorPowerStateChanged,
                InvalidateGamma
            ) ?? Disposable.Null,
            PowerSettingNotification.TryRegister(
                PowerSettingNotification.Ids.AwayModeChanged,
                InvalidateGamma
            ) ?? Disposable.Null,
            SystemEvent.Register(SystemEvent.Ids.DisplayChanged, InvalidateDeviceContexts),
            SystemEvent.Register(SystemEvent.Ids.PaletteChanged, InvalidateDeviceContexts),
            SystemEvent.Register(SystemEvent.Ids.SettingsChanged, InvalidateDeviceContexts),
            SystemEvent.Register(SystemEvent.Ids.SystemColorsChanged, InvalidateDeviceContexts)
        );
    }

    private void EnsureValidDeviceContexts()
    {
        if (_areDeviceContextsValid)
            return;

        _areDeviceContextsValid = true;

        Disposable.Merge(_deviceContexts).Dispose();
        _deviceContexts = Monitor
            .GetAll()
            .Select(m => m.TryCreateDeviceContext())
            .WhereNotNull()
            .ToArray();

        _lastConfiguration = null;
    }

    private bool IsGammaStale()
    {
        var instant = DateTimeOffset.Now;

        // Assume gamma continues to be stale for some time after it has been invalidated.
        // This needs to be reasonably long because some external overrides (e.g. Windows
        // applying its own gamma ramp when the Quick Settings panel is opened for the
        // first time) don't happen immediately after the triggering event, but shortly
        // after it -- so we need to keep re-checking for a while to catch and correct them.
        // https://github.com/Tyrrrz/LightBulb/issues/448
        if ((instant - _lastGammaInvalidationTimestamp).Duration() <= TimeSpan.FromSeconds(2))
        {
            // Avoid spamming gamma updates on frequent invalidation sources (e.g. foreground window changes).
            return (instant - _lastUpdateTimestamp).Duration() >= TimeSpan.FromMilliseconds(200);
        }

        // If polling is enabled, assume gamma is stale after some time has passed since the last update
        if (
            _settingsService.IsGammaPollingEnabled
            && (instant - _lastUpdateTimestamp).Duration() > TimeSpan.FromSeconds(1)
        )
        {
            return true;
        }

        return false;
    }

    private bool IsSignificantChange(ColorConfiguration configuration)
    {
        // Nothing to compare to
        if (_lastConfiguration is not { } lastConfiguration)
            return true;

        return GammaColor.RequiresUpdate(lastConfiguration, configuration);
    }

    public void InvalidateGamma()
    {
        // Don't invalidate gamma when we're in the process of changing it ourselves,
        // to avoid an infinite loop.
        if (_isUpdatingGamma)
            return;

        _lastGammaInvalidationTimestamp = DateTimeOffset.Now;
        Debug.WriteLine("Gamma invalidated.");
    }

    public void InvalidateDeviceContexts()
    {
        _areDeviceContextsValid = false;
        Debug.WriteLine("Device contexts invalidated.");

        InvalidateGamma();
    }

    public void SetGamma(ColorConfiguration configuration)
    {
        // Avoid unnecessary changes as updating too often will cause stuttering
        if (!IsGammaStale() && !IsSignificantChange(configuration))
            return;

        EnsureValidDeviceContexts();

        _isUpdatingGamma = true;

        var color = GammaColor.FromConfiguration(configuration);
        foreach (var deviceContext in _deviceContexts)
        {
            deviceContext.SetGamma(color.Red, color.Green, color.Blue);
        }

        _isUpdatingGamma = false;

        _lastConfiguration = configuration;
        _lastUpdateTimestamp = DateTimeOffset.Now;
        Debug.WriteLine($"Updated gamma to {configuration}.");
    }

    public void Dispose()
    {
        // Reset gamma on all contexts
        foreach (var deviceContext in _deviceContexts)
            deviceContext.ResetGamma();

        _eventSubscription.Dispose();
        Disposable.Merge(_deviceContexts).Dispose();
    }
}
