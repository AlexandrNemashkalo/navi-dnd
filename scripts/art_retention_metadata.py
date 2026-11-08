"""Fingerprint-bound user decisions take precedence over removal suggestions."""
import json


def load_retention_decisions(root):
    path=root/'src/ArtLibrary/retention-decisions.json'
    return json.loads(path.read_text(encoding='utf-8')) if path.exists() else {}


def apply_retention_decision(asset_id, record, decisions):
    decision=decisions.get(asset_id)
    if not decision or decision.get('sha256')!=record.get('sha256'):
        return record
    if decision.get('decision')!='keep':
        raise ValueError(f'Unsupported retention decision: {asset_id}')
    result=dict(record,retentionReview=dict(recommendation='keep',userApproved=True,
                reason=decision['reason']))
    if decision.get('compositionMode'):
        if decision['compositionMode'] not in ('overlay_32','single_32'):
            raise ValueError('Unsupported retained composition mode')
        result['composition']=dict(mode=decision['compositionMode'],canvas=[32,32],fitsSingleTile=True,
            policy='user_approved_upper_parts_only',
            note='Retained by user after removing lower portions; combine surviving layers on one 32x32 canvas without scaling.')
    return result
