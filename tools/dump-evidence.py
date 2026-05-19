"""Dump evidence data from a run into evidence-data.json for sandbox use."""
import sqlite3, json, sys, os

db_path = os.path.join(os.path.dirname(__file__), '..', 'web', 'cyberpilot.db')
conn = sqlite3.connect(db_path)
conn.row_factory = sqlite3.Row
cur = conn.cursor()

# Find the run with the most stage-evidence rows
cur.execute("""
    SELECT e.RunId, COUNT(*) as cnt
    FROM PipelineEvidence e
    WHERE e.Kind = 'stage-evidence'
    GROUP BY e.RunId ORDER BY cnt DESC LIMIT 5
""")
top_runs = [dict(r) for r in cur.fetchall()]
print("Top runs by stage-evidence count:")
for r in top_runs:
    print(f"  {r['RunId']}  ({r['cnt']} rows)")

run_id = sys.argv[1] if len(sys.argv) > 1 else top_runs[0]['RunId']
print(f"\nDumping evidence for run: {run_id}")

cur.execute("""
    SELECT Id, RunId, StageName, Kind, Name, Summary, Uri, MediaType, Source, CreatedAt
    FROM PipelineEvidence
    WHERE RunId = ?
    ORDER BY StageName, Kind, CreatedAt
""", (run_id,))
rows = [dict(r) for r in cur.fetchall()]

from collections import Counter
kinds = Counter(r['Kind'] for r in rows)
print(f"Total rows: {len(rows)}")
print("Kinds:", dict(kinds))
names_stage = Counter(r['Name'][:50] for r in rows if r['Kind'] == 'stage-evidence' and not r['Name'].startswith('tool-output:'))
print("Meaningful stage-evidence names:", dict(list(names_stage.most_common(20))))

out = os.path.join(os.path.dirname(__file__), 'evidence-data.json')
with open(out, 'w') as f:
    json.dump(rows, f, indent=2, default=str)
print(f"\nWrote {len(rows)} rows to {out}")
conn.close()
