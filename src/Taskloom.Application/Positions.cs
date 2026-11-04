namespace Taskloom.Application;

/// <summary>Fractional ordering helpers: an item moved between two neighbours takes their midpoint.</summary>
public static class Positions
{
    public const double Step = 1024;

    /// <summary>
    /// Always finite for finite neighbours. Halving before adding gives the same midpoint as (b + a) / 2 but can't
    /// overflow: two positions near double.MaxValue (any client may PATCH one) would otherwise give Infinity, which JSON
    /// can't carry, and every read of that list would fail.
    /// </summary>
    public static double Between(double? before, double? after) => (before, after) switch
    {
        (null, null) => Step,
        (double b, null) => b + Step,
        (null, double a) => a - Step,
        (double b, double a) => b / 2 + a / 2,
    };
}
