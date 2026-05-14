using Cyberpilot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cyberpilot.Web.Controllers;

internal static class PipelineRunPredicates
{
    public static bool IsDeliveredRun(PipelineRun run)
        => run.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && !run.SkipDeliver;

    public static async Task<bool> IsReviewReworkCandidateAsync(PipelineRun run, CyberpilotDbContext dbContext)
    {
        if (run.IsRemote || run.Status is not ("Failed" or "Stopped"))
        {
            return false;
        }

        if (run.CurrentStage?.Equals("review", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var latestBlockedStage = await dbContext.PipelineStageLogs
            .Where(log => log.RunId == run.Id && (log.Status == "STOP" || log.Status == "INVALID" || log.Status == "failed"))
            .OrderByDescending(log => log.CompletedAt ?? log.StartedAt)
            .Select(log => log.StageName)
            .FirstOrDefaultAsync();

        return latestBlockedStage?.Equals("review", StringComparison.OrdinalIgnoreCase) == true;
    }
}
