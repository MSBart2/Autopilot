<#
.SYNOPSIS
    Pulls aggregate run-level metrics for all Cyberpilot pipeline runs on a benchmark issue.

.DESCRIPTION
    Queries cyberpilot.db for totaled token usage, turns, tool calls, duration, and cost
    across all runs for a given issue number in a repository. Use this for before/after
    comparison across benchmark runs.

.PARAMETER IssueNumber
    GitHub issue number to query runs for.

.PARAMETER Repository
    Repository in "owner/repo" format. Defaults to MSBart2/Aspire1.

.PARAMETER DbPath
    Path to the SQLite database. Defaults to web\cyberpilot.db relative to the repo root.

.EXAMPLE
    .\scripts\Get-IssueBenchmarks.ps1 -IssueNumber 32

.EXAMPLE
    .\scripts\Get-IssueBenchmarks.ps1 -IssueNumber 33 -Repository "MSBart2/Aspire1"
#>
param(
    [Parameter(Mandatory)]
    [int]$IssueNumber,

    [string]$Repository = "MSBart2/Aspire1",

    [string]$DbPath = "$PSScriptRoot\..\web\cyberpilot.db"
)

$DbPath = Resolve-Path $DbPath -ErrorAction Stop

$query = @"
SELECT
  r.Id AS RunId,
  r.CreatedAt,
  r.Status,
  r.Model,
  r.SkipDeliver,
  r.CyberpilotSha,
  r.TargetRepoSha,
  SUM(COALESCE(l.InputTokens, 0))         AS InputTokens,
  SUM(COALESCE(l.OutputTokens, 0))        AS OutputTokens,
  SUM(COALESCE(l.CacheReadTokens, 0))     AS CacheReadTokens,
  SUM(COALESCE(l.CacheWriteTokens, 0))    AS CacheWriteTokens,
  SUM(COALESCE(l.DurationMs, 0))          AS DurationMs,
  SUM(COALESCE(l.TurnCount, 0))           AS Turns,
  SUM(COALESCE(l.ToolCallCount, 0))       AS ToolCalls,
  SUM(COALESCE(l.FailedToolCallCount, 0)) AS FailedToolCalls,
  SUM(COALESCE(l.EstimatedCostUsd, 0))    AS EstimatedCostUsd
FROM PipelineRuns r
JOIN PipelineStageLogs l ON l.RunId = r.Id
WHERE r.Repository = '$Repository'
  AND r.IssueNumber = $IssueNumber
GROUP BY r.Id, r.CreatedAt, r.Status, r.Model, r.SkipDeliver
ORDER BY r.CreatedAt DESC;
"@

$results = sqlite3 $DbPath -separator "`t" -header $query 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "sqlite3 failed: $results"
    exit 1
}

if (-not $results -or $results.Count -le 1) {
    Write-Warning "No runs found for $Repository issue #$IssueNumber"
    exit 0
}

Write-Host "`nBenchmark runs for $Repository #$IssueNumber`n" -ForegroundColor Cyan
$results | ConvertFrom-Csv -Delimiter "`t" | Format-Table -AutoSize
