<#
.SYNOPSIS
    Pulls per-stage metrics for a Cyberpilot pipeline run.

.DESCRIPTION
    Queries cyberpilot.db for detailed stage-level metrics for a specific run ID.
    Use this to inspect token usage, turns, tool calls, duration, and model selection
    for each stage in a benchmark run.

.PARAMETER RunId
    The Cyberpilot run ID (GUID). Copy from the Run Room immediately after dispatch.

.PARAMETER DbPath
    Path to the SQLite database. Defaults to web\cyberpilot.db relative to the repo root.

.EXAMPLE
    .\scripts\Get-RunMetrics.ps1 -RunId "abc123-..."

.EXAMPLE
    .\scripts\Get-RunMetrics.ps1 -RunId "abc123-..." -DbPath "C:\custom\path\cyberpilot.db"
#>
param(
    [Parameter(Mandatory)]
    [string]$RunId,

    [string]$DbPath = "$PSScriptRoot\..\web\cyberpilot.db"
)

$DbPath = Resolve-Path $DbPath -ErrorAction Stop

$query = @"
SELECT
  r.Id AS RunId,
  r.Repository,
  r.IssueNumber,
  r.IssueTitle,
  r.Model AS RequestedModel,
  r.SkipDeliver,
  r.Status AS RunStatus,
  r.CreatedAt,
  r.CompletedAt,
  l.StageName,
  l.Status AS StageStatus,
  l.Model AS ReportedModel,
  l.SelectedModel,
  l.FallbackModel,
  l.InputTokens,
  l.OutputTokens,
  l.CacheReadTokens,
  l.CacheWriteTokens,
  l.ReasoningTokens,
  l.PremiumRequestCost,
  l.EstimatedCostUsd,
  l.DurationMs,
  l.TurnCount,
  l.ToolCallCount,
  l.FailedToolCallCount,
  l.SessionErrorCount,
  l.ReachedIdle,
  l.WasAborted
FROM PipelineRuns r
JOIN PipelineStageLogs l ON l.RunId = r.Id
WHERE r.Id = '$RunId'
ORDER BY l.StartedAt;
"@

$results = sqlite3 $DbPath -separator "`t" -header $query 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "sqlite3 failed: $results"
    exit 1
}

if (-not $results -or $results.Count -le 1) {
    Write-Warning "No stages found for run ID: $RunId"
    exit 0
}

$results | ConvertFrom-Csv -Delimiter "`t" | Format-Table -AutoSize
