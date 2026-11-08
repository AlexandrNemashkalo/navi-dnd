"""Apply fingerprint-bound human annotations without mutating their source."""
import json


def load_reviews(root):
    merged = {}
    for path in (root / 'src/ArtLibrary/reviews.snapshot.json',
                 root / 'artifacts/art-catalog-review/reviews.json',
                 root / 'artifacts/art-review.json',
                 root / 'artifacts/art-catalog-review/art-review.json'):
        if not path.exists():
            continue
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        records = data.get('reviews', data)
        for key, record in records.items():
            if not isinstance(record, dict):
                raise ValueError(f'Invalid review in {path}: {key}')
            if key not in merged or record.get('updatedAt', 0) >= merged[key].get('updatedAt', 0):
                merged[key] = record
    return merged


def apply_review(asset, reviews):
    record = reviews.get(asset['id'])
    if not record:
        matches = [r for r in reviews.values() if r.get('fingerprint') == asset.get('sha256')]
        if len(matches) == 1:
            record = matches[0]
    if not record or record.get('fingerprint') != asset.get('sha256'):
        return asset
    asset = dict(asset, traits=dict(asset.get('traits', {})), humanReview=record)
    for field in ('gender', 'species'):
        value = record.get(field)
        if field == 'gender' and value not in ('male', 'female', 'unknown'):
            continue
        if isinstance(value, str) and value.strip():
            asset[field] = asset['traits'][field] = value.strip()
    return asset
