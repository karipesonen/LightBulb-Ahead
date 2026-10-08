using System;
using System.Linq;

namespace LightBulb.Core;

public sealed record SolarDay(
    DateOnly Date,
    DateTimeOffset Sunrise,
    DateTimeOffset Sunset,
    bool IsManualFallback
)
{
    public static SolarDay Resolve(
        DateOnly date,
        TimeZoneInfo timeZone,
        GeoLocation? location,
        TimeOnly manualSunrise,
        TimeOnly manualSunset
    )
    {
        if (location is { } coordinates)
        {
            var sunrise = SolarTimes.CalculateEvent(coordinates, date, timeZone, true);
            var sunset = SolarTimes.CalculateEvent(coordinates, date, timeZone, false);
            if (sunrise is { } rise && sunset is { } set && rise < set)
                return new(date, rise, set, false);
        }
        return new(
            date,
            ResolveLocalTime(date, manualSunrise, timeZone),
            ResolveLocalTime(date, manualSunset, timeZone),
            location is not null
        );
    }

    public static DateTimeOffset ResolveLocalTime(
        DateOnly date,
        TimeOnly time,
        TimeZoneInfo timeZone
    )
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // A skipped DST clock time moves forward to the first valid minute;
        // an ambiguous clock time uses its first occurrence.
        while (timeZone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new(local, offset);
    }
}
