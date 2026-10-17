"""Evidence failure-path tests: no dotnet execution and no DB connection."""
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, Mock
import regression_evidence as evidence


class EvidenceTests(unittest.TestCase):
    def test_manifest_includes_untracked_and_ignored_source_not_outputs_or_secrets(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'Automation').mkdir()
            (root / 'Automation/new-helper.py').write_text('# new')
            (root / 'Directory.Build.props').write_text('<Project/>')
            for suite in evidence.SUITES:
                name = 'God2.ServerV2.' + suite + ('' if suite == 'Persistence.Integration' else '.Tests')
                if suite == 'Persistence.Integration':
                    name += 'Tests'
                folder = root / 'tests' / name
                folder.mkdir(parents=True)
                (folder / (name + '.csproj')).write_text('<Project/>')
                (folder / 'ignored.cs').write_text('// ignored source')
                (folder / 'bin').mkdir()
                (folder / 'bin/generated.cs').write_text('// generated')
                (folder / 'appsettings.secret.json').write_text('do not read')
            with patch.object(evidence, 'ROOT', root), patch.object(evidence, 'git', return_value=''):
                result = evidence.manifest()
            names = [x['path'] for x in result['files']]
            self.assertEqual(6, sum(x.endswith('/ignored.cs') for x in names))
            self.assertIn('Automation/new-helper.py', names)
            self.assertIn('Directory.Build.props', names)
            self.assertFalse(any('/bin/' in x or 'secret' in x for x in names))
            self.assertTrue(all(not x['tracked'] for x in result['files']))

    def test_add_remove_and_edit_are_unstable(self):
        old = {'files': [{'path': 'edit.cs', 'sha256': 'a'}, {'path': 'remove.cs', 'sha256': 'a'}]}
        new = {'files': [{'path': 'edit.cs', 'sha256': 'b'}, {'path': 'new.cs', 'sha256': 'a'}]}
        self.assertEqual(['edit.cs', 'new.cs', 'remove.cs'], evidence.changes(old, new))

    def test_failed_test_retains_code_trx_and_redacts_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'invocations').mkdir()
            results = root / 'results'
            results.mkdir()
            (results / 'failed.trx').write_text('<TestRun><Counters total="1" failed="1"/></TestRun>')
            process = Mock(stdout=io.StringIO('failure private-value\n'))
            process.wait.return_value = 7
            with patch.dict(os.environ, {'GOD2_REGRESSION_EVIDENCE': tmp}), patch.object(evidence.subprocess, 'Popen', return_value=process):
                code = evidence.run(['dotnet', 'test', 'sample.csproj', '--logger', 'trx;LogFileName=failed.trx', '--results-directory', str(results)],
                                    env={'GOD2_DB_PASSWORD': 'private-value'})
            self.assertEqual(7, code)
            record_path = next((root / 'invocations').glob('*/invocation.json'))
            record = json.loads(record_path.read_text())
            self.assertEqual(7, record['exit_code'])
            self.assertEqual('1', record['trx'][0]['counters']['failed'])
            self.assertNotIn('private-value', (record_path.parent / 'output.log').read_text())
            self.assertTrue((record_path.parent / 'failed.trx').is_file())

    def check_supervisor(self, worker_code, expected):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'docs/handoff').mkdir(parents=True)
            old = {'head': 'a', 'branch': 'branch', 'files': [{'path': 'new.cs', 'sha256': '1'}]}
            new = {**old, 'files': [{'path': 'new.cs', 'sha256': '2'}]}
            process = Mock(stdout=io.StringIO('worker finished\n'))
            process.wait.return_value = worker_code
            cwd = Path.cwd()
            try:
                with patch.object(evidence, 'ROOT', root), patch.object(evidence, 'manifest', side_effect=[old, new]), patch.object(evidence.subprocess, 'Popen', return_value=process):
                    self.assertEqual(expected, evidence.supervise([]))
            finally:
                os.chdir(cwd)
            record = json.loads(next((root / 'docs/handoff').glob('*/run.json')).read_text())
            self.assertEqual(worker_code, record['test_exit_code'])
            self.assertFalse(record['source_stable'])

    def test_finalization_does_not_turn_failure_into_success(self):
        self.check_supervisor(7, 7)

    def test_changed_source_turns_success_into_unstable_failure(self):
        self.check_supervisor(0, 2)

    def test_sensitive_invocation_is_rejected_before_persistence(self):
        with patch.dict(os.environ, {'GOD2_REGRESSION_EVIDENCE': '/not-created'}):
            with self.assertRaises(ValueError):
                evidence.run(['dotnet', 'test', '--password=do-not-record'])


if __name__ == '__main__':
    unittest.main()
