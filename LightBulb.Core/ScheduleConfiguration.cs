using System;
using System.Collections.Generic;
using System.Linq;

namespace LightBulb.Core;

public sealed record ScheduleConfiguration(
    FadeSettings Morning,
    FadeSettings Evening,
    GeoLocation? Location,
    TimeOnly ManualSunrise,
    TimeOnly ManualSunset
)
{
    public ResolvedSchedule Resolve(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);
        var dates = new SortedSet<DateOnly>();
        foreach (
            var anchor in new[]
            {
                instant,
                instant - Morning.FinishOffset,
                instant - Evening.FinishOffset,
            }
        )
        {
            var anchorDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(anchor, timeZone).DateTime
            );
            for (var neighbor = -2; neighbor <= 2; neighbor++)
                dates.Add(anchorDate.AddDays(neighbor));
        }
        var days = dates
            .Select(d => SolarDay.Resolve(d, timeZone, Location, ManualSunrise, ManualSunset))
            .ToArray();
        var fades = Cycle.Resolve(
            days.SelectMany(d => new[] { (d.Sunrise, true), (d.Sunset, false) }),
            Morning,
            Evening
        );
        var today = days.Single(d => d.Date == date);
        var morning = fades.Single(f => f.IsMorning && f.SolarEvent == today.Sunrise);
        var evening = fades.Single(f => !f.IsMorning && f.SolarEvent == today.Sunset);
        return new(today, morning, evening, fades);
    }
}

public sealed record ResolvedSchedule(
    SolarDay Today,
    ResolvedFade Morning,
    ResolvedFade Evening,
    IReadOnlyList<ResolvedFade> Fades
);
