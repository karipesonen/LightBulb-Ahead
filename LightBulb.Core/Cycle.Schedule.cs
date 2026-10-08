using System;
using System.Collections.Generic;
using System.Linq;

namespace LightBulb.Core;

public static partial class Cycle
{
    public static IReadOnlyList<ResolvedFade> Resolve(
        IEnumerable<(DateTimeOffset Instant, bool IsMorning)> solarEvents,
        FadeSettings morning,
        FadeSettings evening
    )
    {
        var events = solarEvents.OrderBy(e => e.Instant).ToArray();
        if (events.Length < 2)
            throw new ArgumentException(
                "At least two dated solar events are required.",
                nameof(solarEvents)
            );
        var fades = new ResolvedFade[events.Length - 1];
        var previous = events[0];
        var previousFinish =
            previous.Instant + (previous.IsMorning ? morning : evening).FinishOffset;
        for (var i = 1; i < events.Length; i++)
        {
            var current = events[i];
            if (current.IsMorning == previous.IsMorning)
                throw new ArgumentException(
                    "Morning and evening solar events must alternate.",
                    nameof(solarEvents)
                );
            var fade = new ResolvedFade(
                current.Instant,
                current.IsMorning,
                current.IsMorning ? morning : evening,
                previousFinish
            );
            fades[i - 1] = fade;
            previous = current;
            previousFinish = fade.Finish;
        }
        return Array.AsReadOnly(fades);
    }

    public static ColorConfiguration InterpolateConfiguration(
        IReadOnlyList<ResolvedFade> schedule,
        ColorConfiguration day,
        ColorConfiguration night,
        DateTimeOffset instant
    )
    {
        if (schedule.Count == 0)
            throw new ArgumentException("A resolved schedule is required.", nameof(schedule));
        foreach (var fade in schedule)
        {
            if (instant >= fade.Finish)
                continue;
            if (instant <= fade.Start)
                return fade.IsMorning ? night : day;
            var progress = (instant - fade.Start) / fade.EffectiveDuration;
            var nightWeight = fade.IsMorning
                ? Math.Cos(progress * Math.PI / 2)
                : Math.Sin(progress * Math.PI / 2);
            return new(
                day.Temperature + (night.Temperature - day.Temperature) * nightWeight,
                day.Brightness + (night.Brightness - day.Brightness) * nightWeight
            );
        }
        return schedule[^1].IsMorning ? day : night;
    }
}
