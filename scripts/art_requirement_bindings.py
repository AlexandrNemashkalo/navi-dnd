"""Offline many-to-many rule-set bindings; never search filenames or pick first."""
import json
import hashlib


def apply_reviewed_usages(root, assets):
    """Promote only explicitly reviewed roles with unchanged image bytes."""
    path = root/'src/ArtLibrary/reviewed-usages.json'
    if not path.exists():
        return
    by_id = {a['id']: a for a in assets}
    for entry in json.loads(path.read_text(encoding='utf-8'))['assets']:
        asset = by_id.get(entry['assetId'])
        if asset and asset.get('sha256') == entry['sha256'] and asset.get('semanticStatus') == 'visually_described':
            asset['usages'] = sorted(set(entry['usages']) if entry.get('replace') else set(asset.get('usages', [])) | set(entry['usages']))
            asset['proposedUsages'] = list(asset['usages'])


def attach_bindings(requirements, assets, document):
    if document.get('schemaVersion') != 1:
        raise ValueError('Unsupported requirement binding schema')
    assets_by_id = {a['id']: a for a in assets}
    rules = {}
    known_requirements={(r['category'],r['name']) for r in requirements}
    for binding in document['bindings']:
        key = (binding['category'], binding['name'])
        if key not in known_requirements:
            raise ValueError(f'Requirement renamed or removed; review binding: {key}')
        if key in rules:
            raise ValueError(f'Duplicate requirement binding: {key}')
        if len({c['assetId'] for c in binding['candidates']}) != len(binding['candidates']):
            raise ValueError(f'Duplicate candidate within {key}')
        rules[key] = binding
    # Exact magic-item artwork is reserved across the entire requirement list.
    # Include content hashes so aliases of the same file cannot evade ownership.
    reserved_ids, reserved_hashes = {}, {}
    for key, binding in rules.items():
        if binding.get('selectionPolicy', {}).get('mode') != 'exclusive_exact':
            continue
        if key[0] != 'magic_item' or len(binding['candidates']) != 1:
            raise ValueError(f'Exact exclusive binding requires one magic-item image: {key}')
        candidate = binding['candidates'][0]
        if candidate.get('match') != 'exact' or not candidate.get('reason', '').strip():
            raise ValueError(f'Exact binding needs an explicit visual justification: {key}')
        for registry, value in [(reserved_ids, candidate['assetId']), (reserved_hashes, candidate['sha256'])]:
            if value in registry:
                raise ValueError(f'Exact image reserved by multiple requirements: {key}')
            registry[value] = key
    for key, binding in rules.items():
        for candidate in binding['candidates']:
            owner = reserved_ids.get(candidate['assetId']) or reserved_hashes.get(candidate['sha256'])
            if owner is not None and owner != key:
                raise ValueError(f'Image reserved for {owner} also used by {key}')
    for requirement in requirements:
        requirement['candidates'],requirement['candidateIssues'] = [],[]
        binding = rules.get((requirement['category'],requirement['name']))
        if not binding:
            continue
        requirement['selectionPolicy'] = binding['selectionPolicy']
        requirement['mappingNote'] = binding['note']
        for candidate in binding['candidates']:
            asset = assets_by_id.get(candidate['assetId'])
            issue = None
            if asset is None:issue='missing_or_user_deleted'
            elif asset.get('sha256') != candidate['sha256']:issue='changed_image'
            elif asset.get('semanticStatus')!='visually_described' and not (
                asset.get('semanticStatus') == 'named_source_match'
                and asset.get('metadataSource') == 'bg3_spell_manifest'
                and requirement['category'] == 'spell' and candidate.get('usage') == 'spell'
                and binding['selectionPolicy'].get('mode') == 'fixed_spell_icon'
                and candidate.get('match') == 'exact' and asset.get('label') == requirement['name']
            ):issue='not_visually_described'
            elif candidate.get('usage') not in asset.get('usages',[]):issue='wrong_usage'
            elif asset.get('humanReview',{}).get('status') in ('rejected','needs_generation'):issue='user_rejected'
            elif asset.get('composition',{}).get('fitsSingleTile') is False:issue='requires_larger_canvas'
            if issue:
                requirement['candidateIssues'].append(dict(assetId=candidate['assetId'],reason=issue))
                continue
            requirement['candidates'].append(dict(candidate,style=asset.get('styleId'),
                identityFamily=asset.get('identityFamily',asset['id']),runtimeApproved=False))
        requirement['status']='candidates_prepared' if requirement['candidates'] else 'needs_mapping'
        requirement['sha256']=hashlib.sha256(json.dumps(dict(id=requirement['id'],name=requirement['name'],
            candidates=requirement['candidates'],selectionPolicy=requirement['selectionPolicy']),sort_keys=True).encode()).hexdigest()


def load_bindings(root):
    path=root/'src/ArtLibrary/RuleSets/Dnd5e/requirement-bindings.json'
    return json.loads(path.read_text(encoding='utf-8')) if path.exists() else dict(schemaVersion=1,bindings=[])


def save_bindings(root, document):
    path = root/'src/ArtLibrary/RuleSets/Dnd5e/requirement-bindings.json'
    text = json.dumps(document, ensure_ascii=False, indent=2) + '\n'
    if path.exists() and path.read_text(encoding='utf-8') == text:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix('.json.new')
    temporary.write_text(text, encoding='utf-8')
    temporary.replace(path)
