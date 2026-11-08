"""Many-to-many bindings preserve variants without reviving removed pictures."""
import json
from pathlib import Path
import unittest
import tempfile
from art_requirement_bindings import attach_bindings,load_bindings,apply_reviewed_usages
from art_spell_assets import merge_spell_bindings

ROOT=Path(__file__).resolve().parents[1]


class RequirementBindingTests(unittest.TestCase):
    def test_pinned_spell_sync_preserves_other_bindings_and_does_not_revive_deleted_art(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root/'src/ArtLibrary/RuleSets/Dnd5e/spell-icons.json'
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps(dict(schemaVersion=2, ruleSet='Dnd5e', usage='spell', icons=[
                dict(id='fireball', name='Fireball'), dict(id='darkness', name='Darkness')])), encoding='utf-8')
            asset = dict(id='bg3-spells:fireball', label='Fireball', sha256='current', usages=['spell'],
                         metadataSource='bg3_spell_manifest', semanticStatus='named_source_match')
            other = dict(category='equipment', name='Shield', candidates=[], note='User notes', selectionPolicy={})
            fallback = dict(other, category='spell', name='Wish')
            old = dict(other, category='spell', name='Fireball', candidates=[dict(assetId='older-guess')])
            document = dict(schemaVersion=1, bindings=[other, fallback, old])
            before = json.dumps(document)
            result = merge_spell_bindings(root, document, [asset])
            self.assertEqual(before, json.dumps(document))
            self.assertEqual([other, fallback], result['bindings'][:2])
            fixed = result['bindings'][2]
            self.assertEqual('fixed_spell_icon', fixed['selectionPolicy']['mode'])
            self.assertEqual(['bg3-spells:fireball'], [c['assetId'] for c in fixed['candidates']])
            self.assertEqual('current', fixed['candidates'][0]['sha256'])
            self.assertEqual([], result['bindings'][3]['candidates'])
            self.assertEqual(result, merge_spell_bindings(root, result, [asset]))

    def test_named_spell_evidence_only_accepts_the_exact_spell_and_respects_rejection(self):
        asset = dict(id='bg3-spells:fireball', label='Fireball', sha256='current', usages=['spell'],
                     metadataSource='bg3_spell_manifest', semanticStatus='named_source_match')
        candidate = dict(assetId=asset['id'], sha256='current', usage='spell', match='exact')
        for category, name, status, count in [('spell', 'Fireball', '', 1), ('spell', 'Burning Hands', '', 0),
                                             ('equipment', 'Fireball', '', 0), ('spell', 'Fireball', 'rejected', 0)]:
            with self.subTest(category=category, name=name, status=status):
                requirements = [dict(id='need', category=category, name=name)]
                binding = dict(category=category, name=name, note='',
                               selectionPolicy=dict(mode='fixed_spell_icon'), candidates=[candidate])
                attach_bindings(requirements, [dict(asset, humanReview=dict(status=status))],
                                dict(schemaVersion=1, bindings=[binding]))
                self.assertEqual(count, len(requirements[0]['candidates']))

    def test_original_bg3_spell_icons_take_precedence_over_thematic_choices(self):
        source = json.loads((ROOT/'src/ArtLibrary/RuleSets/Dnd5e/bg3-spell-sources.json').read_text(encoding='utf-8'))
        by_name = {i['name']: i for i in source['icons']}
        for name in ['Chill Touch', 'Mage Armor', 'Armor of Agathys', 'Find Familiar',
                     'Conjure Minor Elementals', 'Spiritual Weapon', 'TOLL THE DEAD']:
            with self.subTest(name=name):
                self.assertEqual('exact', by_name[name]['mappingKind'])
                self.assertTrue(by_name[name]['gameSpellIds'])
                self.assertTrue(by_name[name]['gameIconNames'])
        self.assertIn('sourceFile', by_name['TOLL THE DEAD'])
        self.assertEqual('thematic', by_name['Frostbite']['mappingKind']) # BG3's same-name item condition is a different effect.
        assets = json.loads((ROOT/'artifacts/art-catalog-review/assets.json').read_text(encoding='utf-8'))
        needs = json.loads((ROOT/'artifacts/art-catalog-review/requirements.json').read_text(encoding='utf-8'))
        attach_bindings(needs, assets, load_bindings(ROOT))
        assets_by_id = {a['id']: a for a in assets}
        for need in needs:
            if need['category'] != 'spell' or not need['candidates']:
                continue
            self.assertEqual(1, len(need['candidates']))
            candidate = need['candidates'][0]
            self.assertTrue(candidate['assetId'].startswith('bg3-spells:'))
            art = assets_by_id[candidate['assetId']]
            expected = 'visual_representation' if art['spellMapping']['kind'] == 'thematic' else 'exact'
            self.assertEqual(expected, candidate['match'])

    def test_reviewed_role_override_is_hash_bound_and_updates_gallery_filter(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); path=root/'src/ArtLibrary/reviewed-usages.json';path.parent.mkdir(parents=True)
            path.write_text(json.dumps(dict(assets=[dict(assetId='mimic',sha256='ok',usages=['map_actor'],replace=True)])),encoding='utf-8')
            current=dict(id='mimic',sha256='ok',semanticStatus='visually_described',usages=['item'],proposedUsages=['item'])
            changed=dict(current,sha256='changed')
            apply_reviewed_usages(root,[current]);apply_reviewed_usages(root,[changed])
            self.assertEqual(['map_actor'],current['usages'])
            self.assertEqual(['map_actor'],current['proposedUsages'])
            self.assertEqual(['item'],changed['usages'])

    def test_exact_magic_art_is_single_and_cannot_be_shared_even_by_hash(self):
        needs=[dict(id='m',category='magic_item',name='special'),dict(id='e',category='equipment',name='ordinary')]
        candidate=dict(assetId='a',sha256='same',usage='item',match='exact',reason='Distinctive visible form')
        exact=dict(category='magic_item',name='special',selectionPolicy=dict(mode='exclusive_exact'),note='',candidates=[candidate])
        assets=[dict(id='a',sha256='same',usages=['item'],semanticStatus='visually_described')]
        attach_bindings(needs,assets,dict(schemaVersion=1,bindings=[exact]))
        self.assertEqual(1,len(needs[0]['candidates']))
        for asset_id in ['a','alias']:
            other=dict(category='equipment',name='ordinary',selectionPolicy={},note='',candidates=[dict(candidate,assetId=asset_id)])
            with self.assertRaisesRegex(ValueError,'reserved'):
                attach_bindings(needs,assets,dict(schemaVersion=1,bindings=[exact,other]))
        with self.assertRaisesRegex(ValueError,'requires one'):
            attach_bindings(needs,assets,dict(schemaVersion=1,bindings=[dict(exact,candidates=[])]))

    def test_multiple_candidates_and_shared_image_are_preserved(self):
        assets=[dict(id=n,sha256=n,usages=['item'],semanticStatus='visually_described') for n in ('a','b')]
        needs=[dict(id=n,category='equipment',name=n) for n in ('one','two')]
        doc=dict(schemaVersion=1,bindings=[dict(category='equipment',name=n,selectionPolicy={},note='',
            candidates=[dict(assetId=k,sha256=k,usage='item') for k in candidates]) for n,candidates in [('one',['a','b']),('two',['a'])]])
        attach_bindings(needs,assets,doc)
        self.assertEqual(['a','b'],[c['assetId'] for c in needs[0]['candidates']])
        self.assertEqual('a',needs[1]['candidates'][0]['assetId'])
        self.assertFalse(needs[0]['candidates'][0]['runtimeApproved'])

    def test_missing_changed_rejected_and_wrong_usage_are_not_substituted(self):
        needs=[dict(id='n',category='equipment',name='one')]
        assets=[dict(id='changed',sha256='new',usages=['item'],semanticStatus='visually_described'),
                dict(id='rejected',sha256='same',usages=['item'],semanticStatus='visually_described',humanReview=dict(status='rejected')),
                dict(id='layer',sha256='same',usages=['layer'],semanticStatus='visually_described')]
        doc=dict(schemaVersion=1,bindings=[dict(category='equipment',name='one',selectionPolicy={},note='',
            candidates=[dict(assetId=k,sha256='same',usage='item') for k in ['missing','changed','rejected','layer']])])
        attach_bindings(needs,assets,doc)
        self.assertEqual([],needs[0]['candidates'])
        self.assertEqual({'missing_or_user_deleted','changed_image','user_rejected','wrong_usage'},
                         {i['reason'] for i in needs[0]['candidateIssues']})

    def test_actual_curated_bindings_reference_reviewed_images_with_source_priority(self):
        assets=json.loads((ROOT/'artifacts/art-catalog-review/assets.json').read_text(encoding='utf-8'))
        needs=json.loads((ROOT/'artifacts/art-catalog-review/requirements.json').read_text(encoding='utf-8'))
        attach_bindings(needs,assets,load_bindings(ROOT))
        mapped=[r for r in needs if r['candidates']]
        self.assertGreater(len(mapped),20)
        self.assertTrue(any(len(r['candidates'])>10 for r in mapped))
        self.assertFalse(any(r['candidateIssues'] for r in mapped))
        candidates=[c for r in mapped for c in r['candidates']]
        self.assertTrue(any(c['assetId'].startswith('dcss/') for c in candidates))
        self.assertTrue(all(c['sourcePriority']==(1 if c['assetId'].startswith('dcss/') else 0) for c in candidates))
        self.assertTrue({'equipment','spell','monster','magic_item'}.issubset({r['category'] for r in mapped}))

    def test_reference_reordering_does_not_change_semantic_binding_or_hide_unknown_names(self):
        asset=dict(id='a',sha256='a',usages=['item'],semanticStatus='visually_described')
        doc=dict(schemaVersion=1,bindings=[dict(category='equipment',name='Shield',note='',selectionPolicy={},
            candidates=[dict(assetId='a',sha256='a',usage='item')])])
        requirements=[dict(id='equipment:99',category='equipment',name='Shield'),
                      dict(id='equipment:0',category='equipment',name='Other')]
        attach_bindings(requirements,[asset],doc)
        self.assertEqual('a',requirements[0]['candidates'][0]['assetId'])
        self.assertEqual([],requirements[1]['candidates'])
        with self.assertRaisesRegex(ValueError,'renamed or removed'):
            attach_bindings([dict(id='equipment:0',category='equipment',name='Renamed')],[asset],doc)

    def test_changing_candidates_invalidates_prior_requirement_review_fingerprint(self):
        assets=[dict(id=k,sha256=k,usages=['item'],semanticStatus='visually_described') for k in ('a','b')]
        requirements=[dict(id='x',category='equipment',name='Shield')]
        binding=dict(category='equipment',name='Shield',note='',selectionPolicy={},candidates=[dict(assetId='a',sha256='a',usage='item')])
        doc=dict(schemaVersion=1,bindings=[binding])
        attach_bindings(requirements,assets,doc);before=requirements[0]['sha256']
        binding['candidates'].append(dict(assetId='b',sha256='b',usage='item'))
        attach_bindings(requirements,assets,doc)
        self.assertNotEqual(before,requirements[0]['sha256'])


if __name__=='__main__':unittest.main()
