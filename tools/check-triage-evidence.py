import json

with open('tools/evidence-data.json') as f:
    rows = json.load(f)

triage = [r for r in rows if r['StageName'] == 'triage'
          and r['Kind'] == 'stage-evidence'
          and not r['Name'].startswith('tool-output:')]
print(f'Triage findings: {len(triage)}')
for r in triage:
    print(f"  [{r['Kind']}] {r['Name']}: {r['Summary'][:100]}")

# Also policy-rationale for triage
policy = [r for r in rows if r['StageName'] == 'triage' and r['Kind'] == 'policy-rationale']
print(f'Triage policy-rationale: {len(policy)}')
for r in policy:
    print(f"  {r['Summary'][:120]}")
