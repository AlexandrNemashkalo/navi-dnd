"""Observed offline captions apply only to the exact reviewed PNG bytes."""
import json


def load_annotations(root):
    result = {}
    for name in ('prepared-annotations.json', 'dcss-annotations.json'):
        path = root / 'src/ArtLibrary' / name
        if not path.exists():
            continue
        records = json.loads(path.read_text(encoding='utf-8'))
        if result.keys() & records.keys():
            raise ValueError('Conflicting visual annotation IDs')
        result.update(records)
    return result


def apply_annotation(asset, annotations):
    record = annotations.get(asset['id'])
    if not record or record.get('sha256') != asset.get('sha256'):
        return asset
    caption = record.get('descriptionEn')
    if not isinstance(caption, str) or not caption.strip():
        raise ValueError(f"Empty observed caption: {asset['id']}")
    # Content observations do not grant art approval or replace user traits.
    result = dict(asset, descriptionEn=caption, semanticStatus='visually_described',
                  metadataSource='visual_detail_review', reviewMethod=record['reviewMethod'])
    for key in ('retentionReview', 'composition', 'reviewSheet', 'reviewPosition'):
        if key in record:
            result[key] = record[key]
    return result
