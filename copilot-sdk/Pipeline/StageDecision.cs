namespace Cyberpilot.Pipeline;

internal static class StageDecision
{
    public const string Approved = "approved";
    public const string ChangesRequested = "changes_requested";

    public static bool IsApproved(StageResult result)
    {
        return result.Decision.Equals(Approved, StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequestsChanges(StageResult result)
    {
        return result.Decision.Equals(ChangesRequested, StringComparison.OrdinalIgnoreCase);
    }
}