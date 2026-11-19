namespace Taskloom.Api.Tests.Support;

/// <summary>A clock tests can move forward; it starts at the real time.</summary>
public sealed class TestClock : TimeProvider
{
    public TimeSpan Offset { get; private set; }

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;

    public void Advance(TimeSpan by) => Offset += by;

    /// <summary>Moves on to the next time it is <paramref name="hour"/>:00 UTC (at least a minute ahead), and returns that day.</summary>
    public DateOnly AdvanceToHourUtc(int hour)
    {
        var now = GetUtcNow();
        var target = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 30, TimeSpan.Zero);
        if (target < now.AddMinutes(1)) target = target.AddDays(1);
        Advance(target - now);
        return DateOnly.FromDateTime(target.UtcDateTime);
    }
}
