using System;
using Xunit;

namespace LightBulb.Core.Tests;

public class SolarDaySpecs
{
    private static TimeZoneInfo Helsinki =>
        TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");

    [Fact]
    public void Polar_absence_uses_saved_manual_times()
    {
        Assert.Null(
            SolarTimes.CalculateEvent(new(69.6489, 18.9551), new(2026, 1, 3), Helsinki, true)
        );
        Assert.Null(
            SolarTimes.CalculateEvent(new(69.6489, 18.9551), new(2026, 1, 3), Helsinki, false)
        );
        var day = SolarDay.Resolve(
            new(2026, 1, 3),
            Helsinki,
            new(69.6489, 18.9551),
            new(7, 20),
            new(16, 30)
        );
        Assert.True(day.IsManualFallback);
        Assert.Equal(new TimeOnly(7, 20), TimeOnly.FromDateTime(day.Sunrise.DateTime));
        Assert.Equal(new TimeOnly(16, 30), TimeOnly.FromDateTime(day.Sunset.DateTime));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(30)]
    public void Each_minute_of_a_year_has_continuous_bounded_output(int hours)
    {
        var morning = new FadeSettings(TimeSpan.FromHours(hours), TimeSpan.FromMinutes(-30));
        var evening = new FadeSettings(TimeSpan.FromHours(hours), TimeSpan.FromMinutes(-90));
        var dayColor = new ColorConfiguration(2500, 1);
        var nightColor = new ColorConfiguration(500, 0.1);
        ColorConfiguration? previous = null;
        for (var dayIndex = 0; dayIndex < 365; dayIndex++)
        {
            var date = new DateOnly(2026, 1, 1).AddDays(dayIndex);
            var start = SolarDay.ResolveLocalTime(date, TimeOnly.MinValue, Helsinki);
            var end = SolarDay.ResolveLocalTime(date.AddDays(1), TimeOnly.MinValue, Helsinki);
            var schedule = new ScheduleConfiguration(
                morning,
                evening,
                new(60.1699, 24.9384),
                new(6, 0),
                new(20, 0)
            )
                .Resolve(start, Helsinki)
                .Fades;
            for (var instant = start; instant < end; instant = instant.AddMinutes(1))
            {
                var color = Cycle.InterpolateConfiguration(schedule, dayColor, nightColor, instant);
                Assert.InRange(color.Temperature, 500, 2500);
                Assert.InRange(color.Brightness, 0.1, 1);
                if (previous is { } last)
                {
                    Assert.InRange(Math.Abs(color.Temperature - last.Temperature), 0, 50);
                    Assert.InRange(Math.Abs(color.Brightness - last.Brightness), 0, 0.025);
                }
                previous = color;
            }
            foreach (var fade in schedule)
            {
                Assert.Equal(
                    fade.IsMorning ? dayColor : nightColor,
                    Cycle.InterpolateConfiguration(schedule, dayColor, nightColor, fade.Finish)
                );
                var lastTemperature = Cycle
                    .InterpolateConfiguration(schedule, dayColor, nightColor, fade.Start)
                    .Temperature;
                for (
                    var instant = fade.Start.AddMinutes(1);
                    instant < fade.Finish;
                    instant = instant.AddMinutes(1)
                )
                {
                    var temperature = Cycle
                        .InterpolateConfiguration(schedule, dayColor, nightColor, instant)
                        .Temperature;
                    Assert.True(
                        fade.IsMorning
                            ? temperature >= lastTemperature
                            : temperature <= lastTemperature
                    );
                    lastTemperature = temperature;
                }
            }
        }
    }

    [Fact]
    public void Polar_schedules_remain_continuous_at_fade_boundaries()
    {
        var morning = new FadeSettings(TimeSpan.FromHours(30), TimeSpan.FromMinutes(-30));
        var evening = new FadeSettings(TimeSpan.FromHours(6), TimeSpan.FromMinutes(-90));
        var day = new ColorConfiguration(2500, 1);
        var night = new ColorConfiguration(500, 0.1);
        foreach (var latitude in new[] { 69.6489, 80, -80 })
        {
            for (var month = 1; month <= 12; month++)
            {
                var instant = SolarDay.ResolveLocalTime(
                    new(2026, month, 15),
                    TimeOnly.MinValue,
                    Helsinki
                );
                var schedule = new ScheduleConfiguration(
                    morning,
                    evening,
                    new(latitude, 18.9551),
                    new(6, 0),
                    new(20, 0)
                ).Resolve(instant, Helsinki);
                foreach (var fade in schedule.Fades)
                {
                    var expected = fade.IsMorning ? day : night;
                    Assert.Equal(
                        expected,
                        Cycle.InterpolateConfiguration(schedule.Fades, day, night, fade.Finish)
                    );
                    var before = Cycle.InterpolateConfiguration(
                        schedule.Fades,
                        day,
                        night,
                        fade.Finish.AddTicks(-1)
                    );
                    Assert.InRange(Math.Abs(before.Temperature - expected.Temperature), 0, 0.001);
                    var atStart = Cycle.InterpolateConfiguration(
                        schedule.Fades,
                        day,
                        night,
                        fade.Start
                    );
                    var afterStart = Cycle.InterpolateConfiguration(
                        schedule.Fades,
                        day,
                        night,
                        fade.Start.AddTicks(1)
                    );
                    Assert.InRange(
                        Math.Abs(afterStart.Temperature - atStart.Temperature),
                        0,
                        0.001
                    );
                }
            }
        }
    }

    [Fact]
    public void Solar_events_use_their_own_DST_offset()
    {
        var before = SolarDay.Resolve(
            new(2026, 3, 28),
            Helsinki,
            new(60.1699, 24.9384),
            new(6, 0),
            new(20, 0)
        );
        var after = SolarDay.Resolve(
            new(2026, 3, 29),
            Helsinki,
            new(60.1699, 24.9384),
            new(6, 0),
            new(20, 0)
        );
        Assert.Equal(TimeSpan.FromHours(2), before.Sunrise.Offset);
        Assert.Equal(TimeSpan.FromHours(3), after.Sunrise.Offset);
        Assert.False(after.IsManualFallback);
        Assert.Equal(new DateTime(2026, 3, 29), after.Sunrise.Date);
    }

    [Fact]
    public void Manual_DST_gap_and_repeated_hour_have_deterministic_resolution()
    {
        var gap = SolarDay.ResolveLocalTime(new(2026, 3, 29), new(3, 30), Helsinki);
        Assert.Equal(new TimeOnly(4, 0), TimeOnly.FromDateTime(gap.DateTime));
        Assert.Equal(TimeSpan.FromHours(3), gap.Offset);
        var repeated = SolarDay.ResolveLocalTime(new(2026, 10, 25), new(3, 30), Helsinki);
        Assert.Equal(TimeSpan.FromHours(3), repeated.Offset);
    }

    [Theory]
    [InlineData(35.6762, 139.6503, "Tokyo Standard Time")]
    [InlineData(40.7128, -74.0060, "Eastern Standard Time")]
    public void Wrapped_UTC_hours_keep_the_requested_local_date(
        double latitude,
        double longitude,
        string zone
    )
    {
        var date = new DateOnly(2026, 1, 15);
        var day = SolarDay.Resolve(
            date,
            TimeZoneInfo.FindSystemTimeZoneById(zone),
            new(latitude, longitude),
            new(6, 0),
            new(20, 0)
        );
        Assert.Equal(date, DateOnly.FromDateTime(day.Sunrise.DateTime));
        Assert.Equal(date, DateOnly.FromDateTime(day.Sunset.DateTime));
        Assert.True(day.Sunrise < day.Sunset);
        Assert.False(day.IsManualFallback);
    }
}
