using System;

namespace LightBulb.Core;

public sealed record ResolvedFade
{
    public DateTimeOffset SolarEvent { get; }
    public DateTimeOffset Start { get; }
    public DateTimeOffset Finish { get; }
    public bool IsMorning { get; }
    public TimeSpan RequestedDuration { get; }
    public TimeSpan EffectiveDuration => Finish - Start;
    public bool IsShortened => EffectiveDuration < RequestedDuration;

    internal ResolvedFade(
        DateTimeOffset solarEvent,
        bool isMorning,
        FadeSettings settings,
        DateTimeOffset precedingFinish
    )
    {
        SolarEvent = solarEvent;
        IsMorning = isMorning;
        Finish = solarEvent + settings.FinishOffset;
        if (Finish <= precedingFinish)
            throw new ArgumentException(
                "Morning and evening targets must remain distinct and alternate."
            );
        RequestedDuration = settings.Duration;
        var available = Finish - precedingFinish;
        Start = Finish - (settings.Duration < available ? settings.Duration : available);
    }
}
