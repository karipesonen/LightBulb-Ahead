using System;
using System.Globalization;
using LightBulb.Core;
using LightBulb.Localization;
using LightBulb.Services;
using PowerKit.Extensions;

namespace LightBulb.ViewModels.Components.Settings;

public class GeneralSettingsTabViewModel(
    SettingsService settingsService,
    LocalizationManager localizationManager
) : SettingsTabViewModelBase(settingsService, localizationManager, 0)
{
    public override string DisplayName => LocalizationManager.GeneralTabName;

    // This value is used for slider bounds, but it's not an actual restriction
    public double RecommendedMaximumDayTemperature => Math.Max(6600, DayTemperature);

    // This value is used for slider bounds, but it's not an actual restriction
    public double RecommendedMinimumDayTemperature => Math.Min(2500, DayTemperature);

    public double DayTemperature
    {
        get => SettingsService.DayConfiguration.Temperature;
        set
        {
            SettingsService.DayConfiguration = new ColorConfiguration(
                value.Clamp(SettingsService.MinimumTemperature, SettingsService.MaximumTemperature),
                DayBrightness
            );

            if (DayTemperature < NightTemperature)
                NightTemperature = DayTemperature;
        }
    }

    // This value is used for slider bounds, but it's not an actual restriction
    public double RecommendedMaximumNightTemperature => Math.Max(6600, NightTemperature);

    // This value is used for slider bounds, but it's not an actual restriction
    public double RecommendedMinimumNightTemperature => Math.Min(2500, NightTemperature);

    public double NightTemperature
    {
        get => SettingsService.NightConfiguration.Temperature;
        set
        {
            SettingsService.NightConfiguration = new ColorConfiguration(
                value.Clamp(SettingsService.MinimumTemperature, SettingsService.MaximumTemperature),
                NightBrightness
            );

            if (NightTemperature > DayTemperature)
                DayTemperature = NightTemperature;
        }
    }

    public double DayBrightness
    {
        get => SettingsService.DayConfiguration.Brightness;
        set
        {
            SettingsService.DayConfiguration = new ColorConfiguration(
                DayTemperature,
                value.Clamp(SettingsService.MinimumBrightness, SettingsService.MaximumBrightness)
            );

            if (DayBrightness < NightBrightness)
                NightBrightness = DayBrightness;
        }
    }

    public double NightBrightness
    {
        get => SettingsService.NightConfiguration.Brightness;
        set
        {
            SettingsService.NightConfiguration = new ColorConfiguration(
                NightTemperature,
                value.Clamp(SettingsService.MinimumBrightness, SettingsService.MaximumBrightness)
            );

            if (NightBrightness > DayBrightness)
                DayBrightness = NightBrightness;
        }
    }

    public TimeSpan MorningDuration
    {
        get => SettingsService.MorningFadeSettings.Duration;
        set => SettingsService.SetFade(true, value, MorningFinishOffsetMinutes);
    }

    public TimeSpan EveningDuration
    {
        get => SettingsService.EveningFadeSettings.Duration;
        set => SettingsService.SetFade(false, value, EveningFinishOffsetMinutes);
    }

    public double MorningFinishOffsetMinutes
    {
        get => SettingsService.MorningFadeSettings.FinishOffset.TotalMinutes;
        set => SettingsService.SetFade(true, MorningDuration, value);
    }

    public double EveningFinishOffsetMinutes
    {
        get => SettingsService.EveningFadeSettings.FinishOffset.TotalMinutes;
        set => SettingsService.SetFade(false, EveningDuration, value);
    }

    public string MorningFinishOffsetText
    {
        get => MorningFinishOffsetMinutes.ToString(CultureInfo.CurrentCulture);
        set => SetOffsetText(true, value);
    }
    public string EveningFinishOffsetText
    {
        get => EveningFinishOffsetMinutes.ToString(CultureInfo.CurrentCulture);
        set => SetOffsetText(false, value);
    }

    private void SetOffsetText(bool isMorning, string value)
    {
        if (
            !double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var minutes)
            || !double.IsFinite(minutes)
        )
        {
            SettingsService.ScheduleError =
                "Enter a finite number of minutes (negative before, positive after).";
            return;
        }
        SettingsService.SetFade(isMorning, isMorning ? MorningDuration : EveningDuration, minutes);
    }

    public string? ScheduleError => SettingsService.ScheduleError;
    public bool HasScheduleError => !string.IsNullOrEmpty(ScheduleError);
    public string MorningScheduleSummary => GetSummary(true);
    public string EveningScheduleSummary => GetSummary(false);

    private string GetSummary(bool isMorning)
    {
        try
        {
            var schedule = SettingsService.ScheduleConfiguration.Resolve(
                DateTimeOffset.Now,
                TimeZoneInfo.Local
            );
            var fade = isMorning ? schedule.Morning : schedule.Evening;
            return string.Format(
                    LocalizationManager.FadeSummary,
                    TimeZoneInfo.ConvertTime(fade.Start, TimeZoneInfo.Local).ToString("t"),
                    TimeZoneInfo.ConvertTime(fade.Finish, TimeZoneInfo.Local).ToString("t"),
                    fade.RequestedDuration.ToString(
                        fade.RequestedDuration.Days == 0 ? @"hh\:mm\:ss" : @"d\.hh\:mm\:ss"
                    ),
                    fade.EffectiveDuration.ToString(
                        fade.EffectiveDuration.Days == 0 ? @"hh\:mm\:ss" : @"d\.hh\:mm\:ss"
                    )
                )
                + (
                    fade.IsShortened
                        ? Environment.NewLine + LocalizationManager.FadeShortened
                        : string.Empty
                )
                + (
                    schedule.Today.IsManualFallback
                        ? Environment.NewLine + LocalizationManager.SolarFallback
                        : string.Empty
                );
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            return error.Message;
        }
    }
}
