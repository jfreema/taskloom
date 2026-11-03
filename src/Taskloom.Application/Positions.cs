namespace Taskloom.Application;

/// <summary>Fractional ordering helpers: an item moved between two neighbours takes their midpoint.</summary>
public static class Positions
{
    public const double Step = 1024;

    public static double Between(double? before, double? after) => (before, after) switch
    {
        (null, null) => Step,
        (double b, null) => b + Step,
        (null, double a) => a - Step,
        (double b, double a) => (b + a) / 2,
    };
}
