namespace Cyberpilot.Pipeline;

internal static class StageStatus
{
    public const string Go = "GO";
    public const string Stop = "STOP";
    public const string Duplicate = "DUPLICATE";

    public static bool IsGo(StageResult result)
    {
        return result.Status.Equals(Go, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStop(StageResult result)
    {
        return result.Status.Equals(Stop, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDuplicate(StageResult result)
    {
        return result.Status.Equals(Duplicate, StringComparison.OrdinalIgnoreCase);
    }
}