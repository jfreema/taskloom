namespace Taskloom.Api.Tests.Support;

/// <summary>A clock tests can move forward; it starts at the real time.</summary>
public sealed class TestClock : TimeProvider
{
    public TimeSpan Offset { get; private set; }

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;

    public void Advance(TimeSpan by) => Offset += by;
}
