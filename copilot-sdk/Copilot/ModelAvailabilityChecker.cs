using GitHub.Copilot.SDK;

namespace Cyberpilot.Copilot;

internal interface IModelAvailabilityChecker
{
    Task<ModelAvailabilityResult> CheckAsync(string model, string repoRoot, CancellationToken cancellationToken = default);
}

internal sealed record ModelAvailabilityResult(bool IsAvailable, string? Error)
{
    public static ModelAvailabilityResult Available { get; } = new(true, null);

    public static ModelAvailabilityResult Unavailable(string error)
    {
        return new ModelAvailabilityResult(false, error);
    }
}

internal sealed class CopilotModelAvailabilityChecker : IModelAvailabilityChecker
{
    public async Task<ModelAvailabilityResult> CheckAsync(string model, string repoRoot, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = new CopilotClient(new CopilotClientOptions
            {
                Cwd = repoRoot,
            });

            await client.StartAsync();
            await using var session = await client.CreateSessionAsync(new SessionConfig
            {
                Model = model,
                Streaming = false,
                OnPermissionRequest = PermissionHandler.ApproveAll,
            });

            return ModelAvailabilityResult.Available;
        }
        catch (Exception ex) when (IsModelUnavailable(ex))
        {
            return ModelAvailabilityResult.Unavailable(ex.Message);
        }
    }

    private static bool IsModelUnavailable(Exception ex)
    {
        return ex.Message.Contains("Model ", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase);
    }
}
