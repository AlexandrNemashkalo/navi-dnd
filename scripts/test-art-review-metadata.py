"""Annotation regressions: user gender data must survive catalogue rebuilds."""
import unittest
from art_review_metadata import apply_review
from art_visual_metadata import apply_annotation
from art_retention_metadata import apply_retention_decision


class ReviewMetadataTests(unittest.TestCase):
    def test_legacy_identifier_resolves_by_exact_fingerprint(self):
        asset = dict(id='new', sha256='abc', traits={'gender': 'unknown'})
        review = dict(fingerprint='abc', gender='female', species='', notes='keep', status='unreviewed')
        result = apply_review(asset, {'legacy': review})
        self.assertEqual('female', result['traits']['gender'])
        self.assertEqual('keep', result['humanReview']['notes'])
        self.assertEqual('unknown', asset['traits']['gender'])
        self.assertNotIn('artApproved', result)

    def test_changed_bytes_do_not_receive_old_gender(self):
        asset = dict(id='same', sha256='new', traits={})
        self.assertEqual(asset, apply_review(asset, {'same': dict(fingerprint='old', gender='male')}))

    def test_ambiguous_fingerprint_never_guesses(self):
        asset = dict(id='new', sha256='abc', traits={})
        reviews = {'a': dict(fingerprint='abc', gender='male'), 'b': dict(fingerprint='abc', gender='female')}
        self.assertNotIn('gender', apply_review(asset, reviews))

    def test_observed_caption_preserves_human_traits_and_art_status(self):
        asset = dict(id='new', sha256='abc', traits={'gender':'female'}, artStatus='needs_in_game_review')
        annotations = {'new':dict(sha256='abc', descriptionEn='Silver curved blade.', reviewMethod='visual')}
        result = apply_annotation(asset, annotations)
        self.assertEqual('Silver curved blade.', result['descriptionEn'])
        self.assertEqual('female', result['traits']['gender'])
        self.assertEqual('needs_in_game_review', result['artStatus'])
        self.assertNotIn('descriptionEn', asset)
        changed = dict(asset, sha256='different')
        self.assertEqual(changed, apply_annotation(changed, annotations))

    def test_composition_recommendation_is_fingerprint_bound_and_not_approval(self):
        asset = dict(id='part',sha256='same',artStatus='pending')
        record = dict(sha256='same',descriptionEn='Upper part of a wing.',reviewMethod='visual',
                      composition=dict(mode='oversized_part',fitsSingleTile=False),
                      retentionReview=dict(recommendation='review_removal',userApproved=False,reason='Too tall'))
        result = apply_annotation(asset, {'part':record})
        self.assertFalse(result['retentionReview']['userApproved'])
        self.assertFalse(result['composition']['fitsSingleTile'])
        self.assertEqual('pending',result['artStatus'])
        self.assertNotIn('composition', apply_annotation(dict(asset,sha256='changed'),{'part':record}))

    def test_user_keep_overrides_old_size_suggestion_and_does_not_restore_lower_parts(self):
        record=dict(sha256='same',composition=dict(mode='oversized_part'),
                    retentionReview=dict(recommendation='review_removal',userApproved=False))
        decisions={'part':dict(sha256='same',decision='keep',reason='User retained upper layers',compositionMode='overlay_32')}
        result=apply_retention_decision('part',record,decisions)
        self.assertEqual('keep',result['retentionReview']['recommendation'])
        self.assertTrue(result['retentionReview']['userApproved'])
        self.assertEqual([32,32],result['composition']['canvas'])
        self.assertEqual('oversized_part',record['composition']['mode'])
        changed=dict(record,sha256='different')
        self.assertEqual(changed,apply_retention_decision('part',changed,decisions))


if __name__ == '__main__':
    unittest.main()
