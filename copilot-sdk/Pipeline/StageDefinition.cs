namespace Cyberpilot.Pipeline;

/// <summary>
/// Describes one Cyberpilot pipeline stage.
/// </summary>
/// <param name="DisplayName">The display name shown to users.</param>
/// <param name="Name">The stable stage key.</param>
/// <param name="PromptFile">The agent prompt file.</param>
/// <param name="Label">The GitHub label used for stage state.</param>
public sealed record StageDefinition(string DisplayName, string Name, string PromptFile, string Label);
