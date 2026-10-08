using System;

namespace LightBulb.Core;

public sealed record FadeSettings
{
    public TimeSpan Duration { get; }
    public TimeSpan FinishOffset { get; }

    public FadeSettings(TimeSpan duration, TimeSpan finishOffset)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        Duration = duration;
        FinishOffset = finishOffset;
    }

    public static (FadeSettings Morning, FadeSettings Evening) FromLegacy(
        TimeSpan duration,
        double offset
    )
    {
        if (!double.IsFinite(offset) || offset is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return (new(duration, duration * offset), new(duration, duration * (1 - offset)));
    }
}
