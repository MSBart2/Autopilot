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
        catch (Exception ex) when (IsModelUnavailable(ex) || IsConnectivityError(ex))
        {
            return ModelAvailabilityResult.Unavailable(BuildMessage(ex));
        }
    }

    private static bool IsModelUnavailable(Exception ex)
    {
        return ex.Message.Contains("Model ", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConnectivityError(Exception ex)
    {
        var current = ex;
        while (current is not null)
        {
            if (current is System.Net.Http.HttpRequestException
                or System.IO.IOException
                or System.Security.Authentication.AuthenticationException)
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    private static string BuildMessage(Exception ex)
    {
        var parts = new System.Text.StringBuilder();
        var current = ex;
        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                if (parts.Length > 0) parts.Append(" → ");
                parts.Append(current.Message);
            }

            current = current.InnerException;
        }

        return parts.Length > 0 ? parts.ToString() : ex.Message;
    }
}
