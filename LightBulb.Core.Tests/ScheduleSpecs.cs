using System;
using System.Linq;
using Xunit;

namespace LightBulb.Core.Tests;

public class ScheduleSpecs
{
    private static readonly DateTimeOffset Midnight = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ColorConfiguration Day = new(2500, 1);
    private static readonly ColorConfiguration Night = new(500, 1);

    private static (DateTimeOffset, bool)[] Events =>
        Enumerable
            .Range(-1, 4)
            .SelectMany(d =>
                new[]
                {
                    (Midnight.AddDays(d).AddHours(7), true),
                    (Midnight.AddDays(d).AddHours(16), false),
                }
            )
            .ToArray();

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(30)]
    public void Long_requested_durations_are_preserved_and_fit_between_targets(int hours)
    {
        var settings = new FadeSettings(TimeSpan.FromHours(hours), TimeSpan.Zero);
        var fades = Cycle.Resolve(Events, settings, settings);
        foreach (var fade in fades)
        {
            Assert.Equal(settings.Duration, fade.RequestedDuration);
            Assert.True(fade.EffectiveDuration <= settings.Duration);
            Assert.Equal(fade.SolarEvent, fade.Finish);
            Assert.Equal(
                fade.IsMorning ? Day : Night,
                Cycle.InterpolateConfiguration(fades, Day, Night, fade.Finish)
            );
        }
        for (var i = 1; i < fades.Count; i++)
            Assert.True(fades[i].Start >= fades[i - 1].Finish);
    }

    [Fact]
    public void Morning_and_evening_have_independent_finish_offsets()
    {
        var morning = new FadeSettings(TimeSpan.FromHours(6), TimeSpan.FromHours(6));
        var evening = new FadeSettings(TimeSpan.FromHours(6), TimeSpan.FromHours(-2));
        var fades = Cycle.Resolve(Events, morning, evening);
        var sunset = Assert.Single(fades, f => !f.IsMorning && f.SolarEvent.Date == Midnight.Date);
        Assert.Equal(Midnight.AddHours(14), sunset.Finish);
        Assert.Equal(Midnight.AddHours(13), sunset.Start);
        Assert.Equal(TimeSpan.FromHours(1), sunset.EffectiveDuration);
        Assert.True(sunset.IsShortened);
        Assert.Equal(Day, Cycle.InterpolateConfiguration(fades, Day, Night, sunset.Start));
        var nextMinute = Cycle.InterpolateConfiguration(
            fades,
            Day,
            Night,
            sunset.Start.AddMinutes(1)
        );
        Assert.InRange(Day.Temperature - nextMinute.Temperature, 0, 60);
    }

    [Fact]
    public void Zero_duration_changes_exactly_at_finish()
    {
        var settings = new FadeSettings(TimeSpan.Zero, TimeSpan.Zero);
        var fades = Cycle.Resolve(Events, settings, settings);
        var finish = Midnight.AddHours(7);
        Assert.Equal(Night, Cycle.InterpolateConfiguration(fades, Day, Night, finish.AddTicks(-1)));
        Assert.Equal(Day, Cycle.InterpolateConfiguration(fades, Day, Night, finish));
    }

    [Fact]
    public void Invalid_duration_and_contradictory_targets_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FadeSettings(TimeSpan.FromTicks(-1), TimeSpan.Zero)
        );
        var morning = new FadeSettings(TimeSpan.FromHours(1), TimeSpan.FromHours(10));
        var evening = new FadeSettings(TimeSpan.FromHours(1), TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => Cycle.Resolve(Events, morning, evening));
    }
}
