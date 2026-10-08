using System;
using PowerKit.Extensions;

namespace LightBulb.Core;

// Times are presented in the current timezone, which is a flimsy convention
public readonly record struct SolarTimes(TimeOnly Sunrise, TimeOnly Sunset)
{
    private static double DegreesToRadians(double degree) => degree * (Math.PI / 180);

    private static double RadiansToDegrees(double radians) => radians * 180 / Math.PI;

    private static double? CalculateUtcHours(
        GeoLocation location,
        DateTimeOffset instant,
        double zenith,
        bool isSunrise,
        bool clampAbsentEvents
    )
    {
        // Based on https://edwilliams.org/sunrise_sunset_algorithm.htm

        // Convert longitude to hour value and calculate an approximate time
        var lngHours = location.Longitude / 15;
        var timeApproxHours = isSunrise ? 6 : 18;
        var timeApproxDays = instant.DayOfYear + (timeApproxHours - lngHours) / 24;

        // Calculate Sun's mean anomaly
        var sunMeanAnomaly = 0.9856 * timeApproxDays - 3.289;

        // Calculate Sun's true longitude
        var sunLng = (
            sunMeanAnomaly
            + 282.634
            + 1.916 * Math.Sin(DegreesToRadians(sunMeanAnomaly))
            + 0.020 * Math.Sin(2 * DegreesToRadians(sunMeanAnomaly))
        ).Wrap(0, 360);

        // Calculate Sun's right ascension
        var sunRightAsc = RadiansToDegrees(Math.Atan(0.91764 * Math.Tan(DegreesToRadians(sunLng))))
            .Wrap(0, 360);

        // Right ascension value needs to be in the same quadrant as true longitude
        var sunLngQuad = Math.Floor(sunLng / 90) * 90;
        var sunRightAscQuad = Math.Floor(sunRightAsc / 90) * 90;
        var sunRightAscHours = (sunRightAsc + (sunLngQuad - sunRightAscQuad)) / 15;

        // Calculate Sun's declination
        var sinDec = 0.39782 * Math.Sin(DegreesToRadians(sunLng));
        var cosDec = Math.Cos(Math.Asin(sinDec));

        // Calculate Sun's zenith local hour
        var sunLocalHoursCos =
            (
                Math.Cos(DegreesToRadians(zenith))
                - sinDec * Math.Sin(DegreesToRadians(location.Latitude))
            ) / (cosDec * Math.Cos(DegreesToRadians(location.Latitude)));
        if (!double.IsFinite(sunLocalHoursCos) || sunLocalHoursCos is < -1 or > 1)
        {
            if (!clampAbsentEvents)
                return null;
            sunLocalHoursCos = Math.Clamp(sunLocalHoursCos, -1, 1);
        }

        // Calculate the local time of Sun's highest point
        var sunLocalHours =
            (
                isSunrise
                    ? 360 - RadiansToDegrees(Math.Acos(sunLocalHoursCos))
                    : RadiansToDegrees(Math.Acos(sunLocalHoursCos))
            ) / 15;

        // Calculate the mean time of the event
        var meanHours = sunLocalHours + sunRightAscHours - 0.06571 * timeApproxDays - 6.622;

        // Adjust mean time to UTC
        return (meanHours - lngHours).Wrap(0, 24);
    }

    private static TimeOnly CalculateSolarTime(
        GeoLocation location,
        DateTimeOffset instant,
        bool isSunrise
    ) =>
        TimeOnly.FromTimeSpan(
            TimeSpan.FromHours(
                (
                    CalculateUtcHours(location, instant, 90.83, isSunrise, true)!.Value
                    + instant.Offset.TotalHours
                ).Wrap(0, 24)
            )
        );

    public static SolarTimes Calculate(GeoLocation location, DateTimeOffset instant) =>
        new(
            CalculateSolarTime(location, instant, true),
            CalculateSolarTime(location, instant, false)
        );

    public static DateTimeOffset? CalculateEvent(
        GeoLocation location,
        DateOnly date,
        TimeZoneInfo timeZone,
        bool isSunrise
    )
    {
        var midnightUtc = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var hours = CalculateUtcHours(location, midnightUtc, 90.83, isSunrise, false);
        if (hours is null)
            return null;
        var utcEvent = midnightUtc.AddHours(hours.Value);
        // UTC's wrapped clock may belong to the previous/next local calendar day.
        for (var delta = -1; delta <= 1; delta++)
        {
            var localEvent = TimeZoneInfo.ConvertTime(utcEvent.AddDays(delta), timeZone);
            if (DateOnly.FromDateTime(localEvent.DateTime) == date)
                return localEvent;
        }
        throw new ArgumentException("Unable to resolve solar event in the selected timezone.");
    }
}
