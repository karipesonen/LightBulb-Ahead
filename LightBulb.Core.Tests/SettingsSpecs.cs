using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia.Data;
using LightBulb.Converters;
using LightBulb.Services;
using Xunit;

namespace LightBulb.Core.Tests;

public class SettingsSpecs
{
    private sealed class SettingsFile(string json) : IDisposable
    {
        public string Path { get; } = Create(json);

        private static string Create(string json)
        {
            var path = System.IO.Path.GetTempFileName();
            File.WriteAllText(path, json);
            return path;
        }

        public void Dispose() => File.Delete(Path);
    }

    [Fact]
    public void First_load_persists_migration_and_second_load_preserves_independent_edits()
    {
        using var file = new SettingsFile(
            """
            {"DayConfiguration":{"Temperature":2500,"Brightness":1},
             "NightConfiguration":{"Temperature":500,"Brightness":1},
             "ConfigurationTransitionDuration":"06:00:00","ConfigurationTransitionOffset":0.25}
            """
        );
        var first = new SettingsService(file.Path);
        Assert.True(first.Load());
        using (var persisted = JsonDocument.Parse(File.ReadAllText(file.Path)))
        {
            Assert.True(persisted.RootElement.TryGetProperty("MorningFade", out _));
            Assert.True(persisted.RootElement.TryGetProperty("EveningFade", out _));
        }
        first.SetFade(false, TimeSpan.FromHours(30), -90);
        Assert.Null(first.ScheduleError);
        first.SaveFile();
        var second = new SettingsService(file.Path);
        Assert.True(second.Load());
        Assert.Equal(first.MorningFade, second.MorningFade);
        Assert.Equal(first.EveningFade, second.EveningFade);
        Assert.Equal(new ColorConfiguration(2500, 1), second.DayConfiguration);
        Assert.Equal(new ColorConfiguration(500, 1), second.NightConfiguration);
    }

    [Theory]
    [InlineData("-01:00:00", "00:00:00")]
    [InlineData("01:00:00", "10:00:00")]
    public void Invalid_saved_schedule_keeps_valid_colors_and_file_until_corrected(
        string duration,
        string offset
    )
    {
        var json = $$$"""
            {"DayConfiguration":{"Temperature":2500,"Brightness":1},
             "NightConfiguration":{"Temperature":500,"Brightness":1},
             "MorningFade":{"Duration":"{{{duration}}}","FinishOffset":"{{{offset}}}"},
             "EveningFade":{"Duration":"01:00:00","FinishOffset":"00:00:00"}}
            """;
        using var file = new SettingsFile(json);
        var settings = new SettingsService(file.Path);
        Assert.True(settings.Load());
        Assert.NotNull(settings.ScheduleError);
        Assert.Equal(json, File.ReadAllText(file.Path));
        settings.Save();
        Assert.Equal(json, File.ReadAllText(file.Path));
        Assert.Equal(new ColorConfiguration(2500, 1), settings.DayConfiguration);
        Assert.Equal(new ColorConfiguration(500, 1), settings.NightConfiguration);
        settings.SetFade(true, TimeSpan.FromHours(6), -30);
        Assert.Null(settings.ScheduleError);
        settings.SaveFile();
        var corrected = new SettingsService(file.Path);
        Assert.True(corrected.Load());
        Assert.Null(corrected.ScheduleError);
        Assert.Equal(TimeSpan.FromHours(6), corrected.MorningFadeSettings.Duration);
    }

    [Fact]
    public void Legacy_JSON_migrates_once_and_preserves_appearance()
    {
        const string legacy = """
            {"DayConfiguration":{"Temperature":2500,"Brightness":1},
             "NightConfiguration":{"Temperature":500,"Brightness":1},
             "ConfigurationTransitionDuration":"06:00:00","ConfigurationTransitionOffset":0.25}
            """;
        var settings = JsonSerializer.Deserialize<SettingsService>(legacy)!;
        Assert.Null(settings.MorningFade);
        settings.MigrateFades();
        Assert.NotNull(settings.MorningFade);
        Assert.NotNull(settings.EveningFade);
        Assert.Equal(TimeSpan.FromHours(1.5), settings.MorningFadeSettings.FinishOffset);
        Assert.Equal(TimeSpan.FromHours(4.5), settings.EveningFadeSettings.FinishOffset);
        settings.EveningFade = new(TimeSpan.FromHours(30), TimeSpan.FromMinutes(-90));
        var saved = JsonSerializer.Serialize(settings);
        var reloaded = JsonSerializer.Deserialize<SettingsService>(saved)!;
        reloaded.MigrateFades();
        Assert.Equal(settings.MorningFade, reloaded.MorningFade);
        Assert.Equal(settings.EveningFade, reloaded.EveningFade);
        Assert.Equal(new ColorConfiguration(2500, 1), reloaded.DayConfiguration);
        Assert.Equal(new ColorConfiguration(500, 1), reloaded.NightConfiguration);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(30)]
    public void Duration_survives_converter_and_JSON_round_trip(int hours)
    {
        var duration = TimeSpan.FromHours(hours);
        var converter = TimeSpanToDurationStringConverter.Instance;
        var text = converter.Convert(duration, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(
            duration,
            converter.ConvertBack(text, typeof(TimeSpan), null, CultureInfo.InvariantCulture)
        );
        var settings = new SettingsService
        {
            MorningFade = new(duration, TimeSpan.Zero),
            EveningFade = new(duration, TimeSpan.Zero),
        };
        var reloaded = JsonSerializer.Deserialize<SettingsService>(
            JsonSerializer.Serialize(settings)
        )!;
        Assert.Equal(duration, reloaded.MorningFadeSettings.Duration);
        Assert.Equal(duration, reloaded.EveningFadeSettings.Duration);
    }

    [Fact]
    public void Maximum_representable_duration_is_not_clamped()
    {
        var settings = new SettingsService();
        settings.MigrateFades();
        settings.SetFade(true, TimeSpan.MaxValue, 0);
        Assert.Null(settings.ScheduleError);
        Assert.Equal(TimeSpan.MaxValue, settings.MorningFadeSettings.Duration);
        var text = TimeSpanToDurationStringConverter.Instance.Convert(
            TimeSpan.MaxValue,
            typeof(string),
            null,
            CultureInfo.InvariantCulture
        );
        Assert.Equal(
            TimeSpan.MaxValue,
            TimeSpanToDurationStringConverter.Instance.ConvertBack(
                text,
                typeof(TimeSpan),
                null,
                CultureInfo.InvariantCulture
            )
        );
        var reloaded = JsonSerializer.Deserialize<SettingsService>(
            JsonSerializer.Serialize(settings)
        )!;
        Assert.Equal(TimeSpan.MaxValue, reloaded.MorningFadeSettings.Duration);
    }

    [Fact]
    public void Malformed_duration_returns_validation_error()
    {
        var result = TimeSpanToDurationStringConverter.Instance.ConvertBack(
            "not a duration",
            typeof(TimeSpan),
            null,
            CultureInfo.InvariantCulture
        );
        Assert.IsType<BindingNotification>(result);
    }

    [Fact]
    public void Invalid_edit_retains_last_valid_fade_and_can_be_corrected()
    {
        var settings = new SettingsService();
        settings.MigrateFades();
        var previous = settings.MorningFade;
        settings.SetFade(true, TimeSpan.FromHours(-1), 0);
        Assert.NotNull(settings.ScheduleError);
        Assert.Equal(previous, settings.MorningFade);
        settings.SetFade(true, TimeSpan.FromHours(30), -30);
        Assert.Null(settings.ScheduleError);
        Assert.Equal(TimeSpan.FromHours(30), settings.MorningFadeSettings.Duration);
        Assert.Equal(TimeSpan.FromMinutes(-30), settings.MorningFadeSettings.FinishOffset);
    }
}
