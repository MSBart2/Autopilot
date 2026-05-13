namespace Cyberpilot.Pipeline;

internal static class BuiltInPolicyProfiles
{
    public static IReadOnlyList<PolicyProfile> All { get; } =
    [
        new("lenient", PolicyStrictness.Lenient),
        new(PipelineDefinitionDefaults.PolicyProfileName, PolicyStrictness.Standard),
        new("strict", PolicyStrictness.Strict),
        new("security-critical", PolicyStrictness.SecurityCritical),
    ];

    public static string AvailableNames => string.Join(", ", All.Select(profile => profile.Name));

    public static bool TryGet(string name, out PolicyProfile profile)
    {
        var selected = All.FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            profile = new PolicyProfile(string.Empty, PolicyStrictness.Standard);
            return false;
        }

        profile = selected;
        return true;
    }
}
