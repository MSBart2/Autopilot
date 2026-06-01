#!/usr/bin/env python3
"""
Cyberpilot Database Query Tool

A reusable CLI tool for querying the Cyberpilot SQLite database.
Supports both pre-built queries and arbitrary SQL.

Usage:
    python tools/db-query.py <command> [options]

Commands:
    runs              Show recent pipeline runs
    stages <run_id>   Show per-stage metrics for a run
    summary <run_id>  Show compact summary of a run
    cost              Show cost breakdown by stage across recent runs
    issues            Show runs grouped by issue
    raw <sql>         Execute arbitrary SQL

Examples:
    python tools/db-query.py runs --limit 5
    python tools/db-query.py stages abc123def456...
    python tools/db-query.py summary abc123def456...
    python tools/db-query.py cost --limit 10
    python tools/db-query.py issues --repo "MSBart2/Aspire1"
    python tools/db-query.py raw "SELECT COUNT(*) FROM PipelineRuns"
"""
import sqlite3
import sys
import os
import argparse
from datetime import datetime

DB_PATH = os.path.join(os.path.dirname(__file__), '..', 'web', 'cyberpilot.db')


def get_connection(db_path=None):
    """Get a database connection with Row factory."""
    path = db_path or DB_PATH
    conn = sqlite3.connect(path)
    conn.row_factory = sqlite3.Row
    return conn


def format_duration(ms):
    """Format milliseconds to human-readable duration."""
    if not ms:
        return "—"
    total_seconds = int(ms / 1000)
    minutes = total_seconds // 60
    seconds = total_seconds % 60
    return f"{minutes}:{seconds:02d}"


def format_cost(value):
    """Format cost value."""
    if value is None:
        return "—"
    return f"${value:.4f}"


def cmd_runs(args):
    """Show recent pipeline runs."""
    conn = get_connection()
    limit = args.limit or 10
    status = args.status

    query = """
        SELECT r.Id, r.IssueNumber, r.IssueTitle, r.Model, r.Status,
               r.CreatedAt, r.CompletedAt, r.PipelineDefinitionName,
               r.BranchName, r.SkipDeliver
        FROM PipelineRuns r
    """
    params = []

    if status:
        query += " WHERE r.Status = ?"
        params.append(status)

    query += " ORDER BY r.CreatedAt DESC LIMIT ?"
    params.append(limit)

    cur = conn.cursor()
    cur.execute(query, params)
    rows = cur.fetchall()

    if not rows:
        print("No runs found.")
        return

    print(f"\n{'='*80}")
    print(f"Recent Pipeline Runs ({len(rows)} shown)")
    print(f"{'='*80}\n")

    for row in rows:
        duration = ""
        if row['CreatedAt'] and row['CompletedAt']:
            started = datetime.fromisoformat(row['CreatedAt'].replace('Z', '+00:00'))
            completed = datetime.fromisoformat(row['CompletedAt'].replace('Z', '+00:00'))
            delta = completed - started
            duration = f"{delta.total_seconds() / 60:.1f}m"

        print(f"Run: {row['Id'][:12]}...")
        print(f"  Issue: #{row['IssueNumber']} - {row['IssueTitle'] or '(no title)'}")
        print(f"  Status: {row['Status']} | Model: {row['Model']}")
        print(f"  Pipeline: {row['PipelineDefinitionName'] or 'default'}")
        print(f"  Branch: {row['BranchName'] or '(none)'}")
        print(f"  Started: {row['CreatedAt'][:19]} | Duration: {duration}")
        print(f"  SkipDeliver: {row['SkipDeliver']}")
        print()

    conn.close()


def cmd_stages(args):
    """Show per-stage metrics for a specific run."""
    conn = get_connection()
    run_id = args.run_id

    # Verify run exists
    cur = conn.cursor()
    cur.execute("SELECT IssueNumber, IssueTitle, Model, Status FROM PipelineRuns WHERE Id = ?", (run_id,))
    run = cur.fetchone()

    if not run:
        print(f"Run {run_id} not found.")
        return

    print(f"\n{'='*80}")
    print(f"Run: {run_id}")
    print(f"Issue: #{run['IssueNumber']} - {run['IssueTitle'] or '(no title)'}")
    print(f"Model: {run['Model']} | Status: {run['Status']}")
    print(f"{'='*80}\n")

    cur.execute("""
        SELECT StageName, Status, Model, InputTokens, OutputTokens,
               CacheReadTokens, CacheWriteTokens, ReasoningTokens,
               EstimatedCostUsd, DurationMs, TurnCount, ToolCallCount,
               FailedToolCallCount, SessionErrorCount, ReachedIdle, WasAborted,
               StartedAt, CompletedAt
        FROM PipelineStageLogs
        WHERE RunId = ?
        ORDER BY StartedAt
    """, (run_id,))

    stages = cur.fetchall()
    total_cost = 0
    total_tokens = 0
    total_duration = 0
    total_turns = 0
    total_tools = 0

    for stage in stages:
        cost = float(stage['EstimatedCostUsd'] or 0)
        input_tokens = int(stage['InputTokens'] or 0)
        output_tokens = int(stage['OutputTokens'] or 0)
        duration = int(stage['DurationMs'] or 0)
        turns = int(stage['TurnCount'] or 0)
        tools = int(stage['ToolCallCount'] or 0)

        total_cost += cost
        total_tokens += input_tokens + output_tokens
        total_duration += duration
        total_turns += turns
        total_tools += tools

        print(f"Stage: {stage['StageName']} | Status: {stage['Status']}")
        print(f"  Model: {stage['Model'] or 'unknown'}")
        print(f"  Tokens: {input_tokens} in / {output_tokens} out | "
              f"Cache: {stage['CacheReadTokens'] or 0} read / {stage['CacheWriteTokens'] or 0} write | "
              f"Reasoning: {stage['ReasoningTokens'] or 0}")
        print(f"  Cost: ${cost:.4f} | Duration: {format_duration(duration)}")
        print(f"  Turns: {turns} | Tools: {tools} | Failed: {stage['FailedToolCallCount'] or 0} | Errors: {stage['SessionErrorCount'] or 0}")
        print(f"  Idle: {stage['ReachedIdle']} | Aborted: {stage['WasAborted']}")
        print()

    print(f"{'─'*80}")
    print(f"TOTALS: Cost: ${total_cost:.4f} | Tokens: {total_tokens} | "
          f"Duration: {format_duration(total_duration)} | Turns: {total_turns} | Tools: {total_tools}")
    print(f"{'='*80}")

    conn.close()


def cmd_summary(args):
    """Show compact summary of a run."""
    conn = get_connection()
    run_id = args.run_id

    cur = conn.cursor()
    cur.execute("""
        SELECT r.Id, r.IssueNumber, r.IssueTitle, r.Model, r.Status,
               r.CreatedAt, r.CompletedAt,
               SUM(COALESCE(l.InputTokens, 0)) as TotalInputTokens,
               SUM(COALESCE(l.OutputTokens, 0)) as TotalOutputTokens,
               SUM(COALESCE(l.CacheReadTokens, 0)) as TotalCacheRead,
               SUM(COALESCE(l.CacheWriteTokens, 0)) as TotalCacheWrite,
               SUM(COALESCE(l.ReasoningTokens, 0)) as TotalReasoning,
               SUM(COALESCE(l.EstimatedCostUsd, 0)) as TotalCost,
               SUM(COALESCE(l.DurationMs, 0)) as TotalDuration,
               SUM(COALESCE(l.TurnCount, 0)) as TotalTurns,
               SUM(COALESCE(l.ToolCallCount, 0)) as TotalTools,
               SUM(COALESCE(l.FailedToolCallCount, 0)) as TotalFailedTools,
               COUNT(l.Id) as StageCount
        FROM PipelineRuns r
        LEFT JOIN PipelineStageLogs l ON l.RunId = r.Id
        WHERE r.Id = ?
        GROUP BY r.Id
    """, (run_id,))

    row = cur.fetchone()

    if not row:
        print(f"Run {run_id} not found.")
        return

    print(f"\n{'='*60}")
    print(f"Run Summary: {row['Id'][:12]}...")
    print(f"{'='*60}")
    print(f"  Issue: #{row['IssueNumber']} - {row['IssueTitle'] or '(no title)'}")
    print(f"  Status: {row['Status']} | Model: {row['Model']}")
    print(f"  Stages: {row['StageCount']}")
    print(f"  Tokens: {row['TotalInputTokens']} in / {row['TotalOutputTokens']} out")
    print(f"  Cache: {row['TotalCacheRead']} read / {row['TotalCacheWrite']} write")
    print(f"  Reasoning: {row['TotalReasoning']}")
    print(f"  Cost: ${row['TotalCost']:.4f}")
    print(f"  Duration: {format_duration(row['TotalDuration'])}")
    print(f"  Turns: {row['TotalTurns']} | Tools: {row['TotalTools']} (failed: {row['TotalFailedTools']})")

    if row['CreatedAt'] and row['CompletedAt']:
        started = datetime.fromisoformat(row['CreatedAt'].replace('Z', '+00:00'))
        completed = datetime.fromisoformat(row['CompletedAt'].replace('Z', '+00:00'))
        wall_time = completed - started
        model_time = row['TotalDuration'] / 1000
        print(f"  Wall Time: {wall_time.total_seconds() / 60:.1f}m | Model Time: {model_time / 60:.1f}m")

    print(f"{'='*60}\n")
    conn.close()


def cmd_cost(args):
    """Show cost breakdown by stage across recent runs."""
    conn = get_connection()
    limit = args.limit or 10

    cur = conn.cursor()
    cur.execute("""
        SELECT r.Id, r.IssueNumber, r.IssueTitle, r.Status,
               l.StageName, l.Model, l.EstimatedCostUsd, l.DurationMs,
               l.InputTokens, l.OutputTokens
        FROM PipelineRuns r
        JOIN PipelineStageLogs l ON l.RunId = r.Id
        WHERE r.Status IN ('Completed', 'Failed', 'Delivered')
        ORDER BY r.CreatedAt DESC
        LIMIT ?
    """, (limit * 8,))  # Assume ~8 stages per run

    rows = cur.fetchall()

    if not rows:
        print("No runs found.")
        return

    # Group by run
    runs = {}
    for row in rows:
        run_id = row['Id']
        if run_id not in runs:
            runs[run_id] = {
                'IssueNumber': row['IssueNumber'],
                'IssueTitle': row['IssueTitle'],
                'Status': row['Status'],
                'stages': []
            }
        runs[run_id]['stages'].append({
            'StageName': row['StageName'],
            'Model': row['Model'],
            'Cost': float(row['EstimatedCostUsd'] or 0),
            'Duration': int(row['DurationMs'] or 0),
            'InputTokens': int(row['InputTokens'] or 0),
            'OutputTokens': int(row['OutputTokens'] or 0),
        })

    print(f"\n{'='*80}")
    print(f"Cost Breakdown (up to {limit} runs)")
    print(f"{'='*80}\n")

    shown = 0
    for run_id, data in runs.items():
        if shown >= limit:
            break

        total_cost = sum(s['Cost'] for s in data['stages'])
        total_tokens = sum(s['InputTokens'] + s['OutputTokens'] for s in data['stages'])

        print(f"Run: {run_id[:12]}... | Issue #{data['IssueNumber']} | {data['Status']}")
        print(f"  {'Stage':<20} {'Model':<25} {'Cost':>10} {'Tokens':>12} {'Duration':>10}")
        print(f"  {'─'*77}")

        for stage in sorted(data['stages'], key=lambda x: float(x['Cost']), reverse=True):
            tokens = int(stage['InputTokens'] or 0) + int(stage['OutputTokens'] or 0)
            print(f"  {stage['StageName']:<20} {(stage['Model'] or '?'):<25} "
                  f"${float(stage['Cost']):>9.4f} {tokens:>12,} {format_duration(stage['Duration']):>10}")

        print(f"  {'─'*77}")
        print(f"  {'TOTAL':<20} {'':<25} ${total_cost:>9.4f} {total_tokens:>12,}")
        print()
        shown += 1

    conn.close()


def cmd_issues(args):
    """Show runs grouped by issue."""
    conn = get_connection()
    repo = args.repo

    query = """
        SELECT r.IssueNumber, r.IssueTitle, r.Id, r.Status, r.Model,
               r.CreatedAt, r.CompletedAt,
               SUM(COALESCE(l.EstimatedCostUsd, 0)) as TotalCost,
               SUM(COALESCE(l.DurationMs, 0)) as TotalDuration,
               COUNT(l.Id) as StageCount
        FROM PipelineRuns r
        LEFT JOIN PipelineStageLogs l ON l.RunId = r.Id
    """
    params = []

    if repo:
        query += " WHERE r.Repository = ?"
        params.append(repo)

    query += """
        GROUP BY r.Id
        ORDER BY r.IssueNumber, r.CreatedAt DESC
        LIMIT 50
    """

    cur = conn.cursor()
    cur.execute(query, params)
    rows = cur.fetchall()

    if not rows:
        print("No runs found.")
        return

    # Group by issue
    issues = {}
    for row in rows:
        issue_num = row['IssueNumber']
        if issue_num not in issues:
            issues[issue_num] = {
                'Title': row['IssueTitle'],
                'runs': []
            }
        issues[issue_num]['runs'].append({
            'Id': row['Id'],
            'Status': row['Status'],
            'Model': row['Model'],
            'Cost': row['TotalCost'] or 0,
            'Duration': row['TotalDuration'] or 0,
            'Stages': row['StageCount'],
        })

    print(f"\n{'='*80}")
    print(f"Runs by Issue ({len(issues)} issues)")
    print(f"{'='*80}\n")

    for issue_num, data in issues.items():
        total_cost = sum(r['Cost'] for r in data['runs'])
        print(f"Issue #{issue_num}: {data['Title'] or '(no title)'} ({len(data['runs'])} runs)")
        print(f"  {'Run':<14} {'Status':<12} {'Model':<25} {'Cost':>10} {'Duration':>10} {'Stages':>7}")
        print(f"  {'─'*88}")

        for run in data['runs']:
            print(f"  {run['Id'][:12]:<14} {run['Status']:<12} {(run['Model'] or '?'):<25} "
                  f"${run['Cost']:>9.4f} {format_duration(run['Duration']):>10} {run['Stages']:>7}")

        print(f"  {'─'*88}")
        print(f"  {'':<14} {'':<12} {'':<25} ${total_cost:>9.4f}")
        print()

    conn.close()


def cmd_raw(args):
    """Execute arbitrary SQL."""
    conn = get_connection()
    sql = args.sql

    cur = conn.cursor()
    try:
        cur.execute(sql)
        rows = cur.fetchall()

        if not rows:
            print("No results.")
            return

        # Print headers
        headers = [desc[0] for desc in cur.description]
        print("\t".join(headers))
        print("\t" + "-" * (len("\t".join(headers)) - 1))

        # Print rows
        for row in rows:
            print("\t".join(str(v) for v in row))

    except Exception as e:
        print(f"SQL Error: {e}", file=sys.stderr)
        return

    conn.close()


def main():
    parser = argparse.ArgumentParser(
        description="Cyberpilot Database Query Tool",
        formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument("--db", help="Path to SQLite database (default: web/cyberpilot.db)")

    subparsers = parser.add_subparsers(dest="command", help="Command to run")

    # runs command
    runs_parser = subparsers.add_parser("runs", help="Show recent pipeline runs")
    runs_parser.add_argument("--limit", type=int, help="Number of runs to show (default: 10)")
    runs_parser.add_argument("--status", help="Filter by status (e.g. Completed, Failed, Running)")

    # stages command
    stages_parser = subparsers.add_parser("stages", help="Show per-stage metrics for a run")
    stages_parser.add_argument("run_id", help="Run ID (GUID)")

    # summary command
    summary_parser = subparsers.add_parser("summary", help="Show compact summary of a run")
    summary_parser.add_argument("run_id", help="Run ID (GUID)")

    # cost command
    cost_parser = subparsers.add_parser("cost", help="Show cost breakdown by stage")
    cost_parser.add_argument("--limit", type=int, help="Number of runs to show (default: 10)")

    # issues command
    issues_parser = subparsers.add_parser("issues", help="Show runs grouped by issue")
    issues_parser.add_argument("--repo", help="Filter by repository (e.g. MSBart2/Aspire1)")

    # raw command
    raw_parser = subparsers.add_parser("raw", help="Execute arbitrary SQL")
    raw_parser.add_argument("sql", help="SQL query to execute")

    args = parser.parse_args()

    if not args.command:
        parser.print_help()
        return

    commands = {
        "runs": cmd_runs,
        "stages": cmd_stages,
        "summary": cmd_summary,
        "cost": cmd_cost,
        "issues": cmd_issues,
        "raw": cmd_raw,
    }

    commands[args.command](args)


if __name__ == "__main__":
    main()