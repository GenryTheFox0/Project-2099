"""Regression tests for discarded embedded-dialogue normalization."""
import copy
import sys
import unittest
from unittest import mock

import build_patch_index as builder
import add_revision


def patch(path, source='A', target='B'):
    return {'Path': path, 'SourceSize': 1, 'SourceSha256': source * 64,
            'TargetSize': 1, 'TargetSha256': target * 64,
            'Delta': 'original/x.eotp', 'DeltaSize': 1, 'DeltaSha256': 'D' * 64}


def manifest(path, digest):
    return {'Files': [{'Path': path, 'Sha256': digest * 64, 'Size': 1}]}


class PatchIndexTests(unittest.TestCase):
    level = 'Data/01A_SMA_010_PortalRoom_FlashBack.pkz'

    def test_embedded_dialogue_kept_without_russian_delta(self):
        sets = {'eu-retail': {'original': [], 'russian': []},
                'sazanoff': {'original': [patch(self.level)], 'russian': []}}
        index, _ = builder.build_index(sets)
        self.assertEqual(index['English'][0]['Path'], self.level)
        self.assertFalse(index['CanonicalTargetsVerified'])

    def test_missing_known_normalization_is_not_complete(self):
        sets = {'eu-retail': {'original': [patch(self.level)], 'russian': []}}
        with self.assertRaisesRegex(ValueError, 'Missing normalization'):
            builder.validate_coverage({'English': []}, sets)

    def test_historical_wrong_canonical_target_is_rejected(self):
        sets = {'eu-retail': {'original': [patch(self.level)], 'russian': []}}
        with self.assertRaisesRegex(ValueError, 'Unverified English target'):
            builder.build_index(sets, expected_original=manifest(self.level, 'C'),
                                expected_russian=manifest(self.level, 'C'),
                                source_manifests={'eu-retail': manifest(self.level, 'A')})

    def test_unknown_passthrough_is_rejected(self):
        sets = {'eu-retail': {'original': [], 'russian': []}}
        with self.assertRaisesRegex(ValueError, 'Unverified Original passthrough'):
            builder.build_index(sets, expected_original=manifest(self.level, 'B'),
                                expected_russian=manifest(self.level, 'B'),
                                source_manifests={'eu-retail': manifest(self.level, 'A')})

    def test_verified_two_stage_route(self):
        sets = {'eu-retail': {'original': [patch(self.level)],
                             'russian': [patch(self.level, 'B', 'C')]}}
        index, _ = builder.build_index(sets, expected_original=manifest(self.level, 'B'),
            expected_russian=manifest(self.level, 'C'),
            source_manifests={'eu-retail': manifest(self.level, 'A')})
        self.assertTrue(index['CanonicalTargetsVerified'])

    def test_corrective_chain_then_russian_inverse_preserves_russian(self):
        sets = {'eu-retail': {'original': [patch(self.level, 'A', 'B')], 'russian': []}}
        corrections = {'English': {'Root': 'qa', 'Files': [patch(self.level, 'B', 'C')]},
                       'Russian': {'Root': 'qa', 'Files': [patch(self.level, 'C', 'B')]}}
        index, _ = builder.build_index(sets, expected_original=manifest(self.level, 'C'),
            expected_russian=manifest(self.level, 'B'),
            source_manifests={'eu-retail': manifest(self.level, 'A')}, corrections=corrections)
        self.assertTrue(index['CanonicalTargetsVerified'])
        self.assertEqual(len(index['English']), 2)

    def test_cycle_in_normalization_fails(self):
        sets = {'eu-retail': {'original': [patch(self.level, 'A', 'B'),
                                         patch(self.level, 'B', 'A')], 'russian': []}}
        with self.assertRaisesRegex(ValueError, 'Cyclic'):
            builder.build_index(sets)

    def test_rebuilt_set_and_same_correction_are_deduplicated(self):
        sets = {'eu-retail': {'original': [], 'russian': [patch(self.level, 'C', 'B')]}}
        corrections = {'Russian': {'Root': 'qa', 'Files': [patch(self.level, 'C', 'B')]}}
        index, _ = builder.build_index(sets, corrections=corrections)
        self.assertEqual(len(index['Russian']), 1)

    def test_size_mismatch_between_chain_steps_fails(self):
        wrong = patch(self.level, 'B', 'C')
        wrong['SourceSize'] = 2
        sets = {'eu-retail': {'original': [patch(self.level), wrong], 'russian': []}}
        with self.assertRaisesRegex(ValueError, 'source size mismatch'):
            builder.build_index(sets)

    def test_legacy_revision_tool_refuses_schema4_before_reading_donor(self):
        with mock.patch.object(sys, 'argv', ['add_revision.py', '--dump', 'unused']), \
                mock.patch.object(add_revision, 'load', return_value={'Schema': 4}), \
                mock.patch.object(add_revision.os.path, 'isfile', side_effect=AssertionError('Donor touched')):
            with self.assertRaisesRegex(SystemExit, 'cannot modify schema 4'):
                add_revision.main()

    def test_actual_patchsets_cover_every_normalization(self):
        sets = {v: {lane: builder.load(builder.BASE / 'patchsets' / v / (lane + '.json'))['Files']
                    for lane in ('original', 'russian')} for v in builder.VARIANTS}
        index, _ = builder.build_index(sets)
        builder.validate_coverage(index, sets)
        for variant in ('sazanoff-rus-god', 'ru-god-alt'):
            expected = next(e for e in sets[variant]['original'] if e['Path'] == self.level)
            self.assertTrue(any(builder.key(e['Path'], e['SourceSha256']) ==
                                builder.key(expected['Path'], expected['SourceSha256'])
                                for e in index['English']))
        broken = copy.deepcopy(index)
        broken['English'] = [e for e in broken['English'] if e['Path'] != self.level]
        with self.assertRaisesRegex(ValueError, 'Missing normalization'):
            builder.validate_coverage(broken, sets)


if __name__ == '__main__':
    unittest.main()
