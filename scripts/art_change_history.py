"""Persistent requirement deltas; rebuilds without semantic changes are silent."""
import json
from datetime import datetime, timezone


def update_history(path, requirements):
    state = json.loads(path.read_text(encoding='utf-8')) if path.exists() else {'snapshot': {}, 'batches': []}
    current = {}
    for r in requirements:
        if r.get('candidates'):
            key = json.dumps([r['category'], r['name']], ensure_ascii=False)
            current[key] = dict(category=r['category'], name=r['name'], candidates=r['candidates'],
                                selectionPolicy=r.get('selectionPolicy'), note=r.get('mappingNote'))
    events = []
    for key in sorted(current.keys() | state['snapshot'].keys()):
        old, new = state['snapshot'].get(key), current.get(key)
        if old == new:
            continue
        value = new or old
        before = {c['assetId'] for c in (old or {}).get('candidates', [])}
        after = {c['assetId'] for c in (new or {}).get('candidates', [])}
        events.append(dict(category=value['category'], name=value['name'],
                           kind='added' if old is None else 'removed' if new is None else 'updated',
                           added=sorted(after-before), removed=sorted(before-after)))
    if events:
        state['batches'].insert(0, dict(id=str(len(state['batches'])+1),
            at=datetime.now(timezone.utc).isoformat(), events=events))
    state['snapshot'] = current
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix('.json.new')
    temporary.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding='utf-8')
    temporary.replace(path)
    return state['batches']
