using System;
using LightBulb.Core;
using LightBulb.Localization;
using LightBulb.Services;
using LightBulb.ViewModels.Components.Settings;
using Xunit;

namespace LightBulb.Core.Tests;

public class GeneralSettingsUiSpecs
{
    [Fact]
    public void Fade_summary_uses_clock_times_and_whole_second_durations()
    {
        var settings = new SettingsService
        {
            IsManualSunriseSunsetEnabled = true,
            MorningFade = new FadeSettings(TimeSpan.Zero, TimeSpan.Zero),
            EveningFade = new FadeSettings(
                TimeSpan.FromHours(30) + TimeSpan.FromTicks(123),
                TimeSpan.Zero
            ),
        };
        using var localization = new LocalizationManager(settings);
        using var viewModel = new GeneralSettingsTabViewModel(settings, localization);
        var fade = settings
            .ScheduleConfiguration.Resolve(DateTimeOffset.Now, TimeZoneInfo.Local)
            .Evening;

        Assert.Contains(
            TimeZoneInfo.ConvertTime(fade.Start, TimeZoneInfo.Local).ToString("t"),
            viewModel.EveningScheduleSummary
        );
        Assert.DoesNotContain(DateTimeOffset.Now.ToString("d"), viewModel.EveningScheduleSummary);
        Assert.Contains("1.06:00:00", viewModel.EveningScheduleSummary);
        Assert.DoesNotContain("0000123", viewModel.EveningScheduleSummary);
        Assert.False(viewModel.HasScheduleError);
        settings.ScheduleError = "Invalid schedule";
        Assert.True(viewModel.HasScheduleError);
    }
}
