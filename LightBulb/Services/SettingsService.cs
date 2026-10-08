using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cogwheel;
using CommunityToolkit.Mvvm.ComponentModel;
using LightBulb.Core;
using LightBulb.Framework;
using LightBulb.Localization;
using LightBulb.Models;
using LightBulb.PlatformInterop;
using Microsoft.Win32;

namespace LightBulb.Services;

[ObservableObject]
public partial class SettingsService : SettingsBase
{
    private readonly string _settingsPath;

    public SettingsService()
        : this(StartOptions.Current.SettingsPath) { }

    internal SettingsService(string settingsPath)
        : base(StartOptions.ValidateSettingsPath(settingsPath), SerializerContext.Default)
    {
        _settingsPath = StartOptions.ValidateSettingsPath(settingsPath);
    }

    private readonly RegistrySwitch<int> _extendedGammaRangeSwitch = new(
        RegistryHive.LocalMachine,
        @"Software\Microsoft\Windows NT\CurrentVersion\ICM",
        "GdiICMGammaRange",
        256
    );

    private readonly RegistrySwitch<string> _autoStartSwitch = new(
        RegistryHive.CurrentUser,
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        Program.Name,
        $"\"{Program.ExecutableFilePath}\" {StartOptions.IsInitiallyHiddenArgument}"
    );

    [ObservableProperty]
    public partial bool IsFirstTimeExperienceEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsUkraineSupportMessageEnabled { get; set; } = true;

    [ObservableProperty]
    [JsonIgnore] // comes from registry
    public partial bool IsExtendedGammaRangeUnlocked { get; set; }

    // General

    public double MinimumTemperature => 500;

    public double MaximumTemperature => 20_000;

    public double MinimumBrightness => 0.1;

    public double MaximumBrightness => 1;

    [ObservableProperty]
    public partial ColorConfiguration DayConfiguration { get; set; } = new(6600, 1);

    [ObservableProperty]
    public partial ColorConfiguration NightConfiguration { get; set; } = new(3900, 0.85);

    [ObservableProperty]
    public partial TimeSpan ConfigurationTransitionDuration { get; set; } =
        TimeSpan.FromMinutes(40);

    [ObservableProperty]
    public partial double ConfigurationTransitionOffset { get; set; }

    [ObservableProperty]
    public partial FadeSettings? MorningFade { get; set; }

    [ObservableProperty]
    public partial FadeSettings? EveningFade { get; set; }

    [JsonIgnore]
    public FadeSettings MorningFadeSettings =>
        MorningFade
        ?? FadeSettings
            .FromLegacy(ConfigurationTransitionDuration, ConfigurationTransitionOffset)
            .Morning;

    [JsonIgnore]
    public FadeSettings EveningFadeSettings =>
        EveningFade
        ?? FadeSettings
            .FromLegacy(ConfigurationTransitionDuration, ConfigurationTransitionOffset)
            .Evening;

    [JsonIgnore]
    public ScheduleConfiguration ScheduleConfiguration =>
        new(
            MorningFadeSettings,
            EveningFadeSettings,
            IsManualSunriseSunsetEnabled ? null : Location,
            ManualSunrise,
            ManualSunset
        );

    [ObservableProperty]
    [JsonIgnore]
    public partial string? ScheduleError { get; set; }

    public void SetFade(bool isMorning, TimeSpan duration, double finishOffsetMinutes)
    {
        try
        {
            var fade = new FadeSettings(duration, TimeSpan.FromMinutes(finishOffsetMinutes));
            var configuration = isMorning
                ? ScheduleConfiguration with
                {
                    Morning = fade,
                }
                : ScheduleConfiguration with
                {
                    Evening = fade,
                };
            configuration.Resolve(DateTimeOffset.Now, TimeZoneInfo.Local);
            if (isMorning)
                MorningFade = fade;
            else
                EveningFade = fade;
            ScheduleError = null;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            ScheduleError = error.Message;
        }
    }

    internal bool MigrateFades()
    {
        if (MorningFade is not null && EveningFade is not null)
            return false;
        var legacy = FadeSettings.FromLegacy(
            ConfigurationTransitionDuration,
            ConfigurationTransitionOffset
        );
        MorningFade ??= legacy.Morning;
        EveningFade ??= legacy.Evening;
        return true;
    }

    [ObservableProperty]
    public partial TimeSpan ConfigurationSmoothingMaxDuration { get; set; } =
        TimeSpan.FromSeconds(5);

    // Location

    [ObservableProperty]
    public partial bool IsManualSunriseSunsetEnabled { get; set; } = true;

    [ObservableProperty]
    [JsonPropertyName("ManualSunriseTime")]
    public partial TimeOnly ManualSunrise { get; set; } = new(07, 20);

    [ObservableProperty]
    [JsonPropertyName("ManualSunsetTime")]
    public partial TimeOnly ManualSunset { get; set; } = new(16, 30);

    [ObservableProperty]
    public partial GeoLocation? Location { get; set; }

    // Advanced

    [ObservableProperty]
    public partial ThemeVariant Theme { get; set; }

    [ObservableProperty]
    public partial Language Language { get; set; }

    [ObservableProperty]
    [JsonIgnore] // comes from registry
    public partial bool IsAutoStartEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsAutoUpdateEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsDefaultToDayConfigurationEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsConfigurationSmoothingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsPauseWhenFullScreenEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsGammaPollingEnabled { get; set; }

    // Application whitelist

    [ObservableProperty]
    public partial bool IsApplicationWhitelistEnabled { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ExternalApplication>? WhitelistedApplications { get; set; }

    // HotKeys

    [ObservableProperty]
    public partial HotKey ToggleHotKey { get; set; }

    [ObservableProperty]
    [JsonPropertyName("FocusWindowHotKey")]
    public partial HotKey ToggleWindowHotKey { get; set; }

    [ObservableProperty]
    public partial HotKey IncreaseTemperatureOffsetHotKey { get; set; }

    [ObservableProperty]
    public partial HotKey DecreaseTemperatureOffsetHotKey { get; set; }

    [ObservableProperty]
    public partial HotKey IncreaseBrightnessOffsetHotKey { get; set; }

    [ObservableProperty]
    public partial HotKey DecreaseBrightnessOffsetHotKey { get; set; }

    [ObservableProperty]
    public partial HotKey ResetConfigurationOffsetHotKey { get; set; }

    public override void Reset()
    {
        base.Reset();
        ScheduleError = null;
        MigrateFades();

        // Don't reset the first-time experience
        IsFirstTimeExperienceEnabled = false;
        IsUkraineSupportMessageEnabled = false;

        // Trigger UI updates
        OnPropertyChanged(string.Empty);
    }

    public override void Save()
    {
        if (ScheduleError is not null)
            return;
        // Disallow auto-start in debug mode to make things simpler
#if DEBUG
        IsAutoStartEnabled = false;
#endif

        MigrateFades();
        base.Save();

        // Update values in the registry
        try
        {
            _extendedGammaRangeSwitch.IsSet = IsExtendedGammaRangeUnlocked;
            _autoStartSwitch.IsSet = IsAutoStartEnabled;
        }
        catch (Win32Exception)
        {
            // This can happen if the user doesn't have the necessary permissions to update
            // the corresponding registry keys, and privilege elevation has failed.
            // Throwing an exception here is very messy, so we'll just ignore it.
            // https://github.com/Tyrrrz/LightBulb/issues/335
        }

        // Trigger UI updates
        OnPropertyChanged(string.Empty);
    }

    public override bool Load()
    {
        var path = _settingsPath;
        var originalDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LightBulb"
        );
        var originalPath = Path.Combine(originalDirectory, "Settings.json");
        if (!File.Exists(path) && File.Exists(originalPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(originalPath, path, false);
        }
        var previousMorning = MorningFadeSettings;
        var previousEvening = EveningFadeSettings;
        ScheduleError = null;
        var wasLoaded = File.Exists(path);
        if (wasLoaded)
        {
            try
            {
                using var document = JsonDocument.Parse(
                    File.ReadAllBytes(path),
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                    }
                );
                LoadDocument(document.RootElement);
            }
            catch (JsonException error)
            {
                ScheduleError = error.Message;
            }
        }
        var migrated = MigrateFades();
        try
        {
            ScheduleConfiguration.Resolve(DateTimeOffset.Now, TimeZoneInfo.Local);
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            MorningFade = previousMorning;
            EveningFade = previousEvening;
            ScheduleError = error.Message;
        }
        IsAutoUpdateEnabled = false;
        if (wasLoaded && migrated && ScheduleError is null)
            base.Save();

        // Get values from the registry
        IsExtendedGammaRangeUnlocked = _extendedGammaRangeSwitch.IsSet;
        IsAutoStartEnabled = _autoStartSwitch.IsSet;

        // Trigger UI updates
        OnPropertyChanged(string.Empty);

        return wasLoaded;
    }

    private void LoadDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Settings must be a JSON object.");
        var needsLegacy =
            !root.TryGetProperty(nameof(MorningFade), out var morning)
            || morning.ValueKind == JsonValueKind.Null
            || !root.TryGetProperty(nameof(EveningFade), out var evening)
            || evening.ValueKind == JsonValueKind.Null;
        var typeInfo = SerializerContext.Default.SettingsService;
        foreach (var jsonProperty in root.EnumerateObject())
        {
            var isLegacy =
                jsonProperty.Name
                is nameof(ConfigurationTransitionDuration)
                    or nameof(ConfigurationTransitionOffset);
            if (isLegacy && !needsLegacy)
                continue;
            var property = typeInfo.Properties.FirstOrDefault(p => p.Name == jsonProperty.Name);
            if (property?.Set is null)
                continue;
            try
            {
                var value = jsonProperty.Value.Deserialize(
                    SerializerContext.Default.GetTypeInfo(property.PropertyType)!
                );
                if (
                    isLegacy
                    && (
                        value is TimeSpan duration && duration < TimeSpan.Zero
                        || value is double offset
                            && (!double.IsFinite(offset) || offset is < 0 or > 1)
                    )
                )
                    throw new ArgumentException("Invalid legacy fade settings.");
                property.Set(this, value);
            }
            catch (Exception error)
                when (error is JsonException or ArgumentException or OverflowException)
            {
                ScheduleError = $"{jsonProperty.Name}: {error.Message}";
            }
        }
    }

    internal void SaveFile() => base.Save();
}

public partial class SettingsService
{
    [JsonSerializable(typeof(SettingsService))]
    private partial class SerializerContext : JsonSerializerContext;
}
