"""Evidence failure-path tests: no dotnet execution and no DB connection."""
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, Mock
import regression_evidence as evidence
import regression_db_state as db_state
import run_v2_isolated as worker
import re


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

    def test_success_process_requires_complete_passing_trx(self):
        for contents in (None, '<broken', '<TestRun/>',
                         '<TestRun><Counters total="oops"/></TestRun>',
                         '<TestRun><Counters total="1" executed="1" passed="0" failed="1" notExecuted="0"/></TestRun>'):
            with self.subTest(contents=contents), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                (root / 'invocations').mkdir()
                results = root / 'results'
                results.mkdir()
                if contents is not None:
                    (results / 'result.trx').write_text(contents)
                process = Mock(stdout=io.StringIO('finished\n'))
                process.wait.return_value = 0
                with patch.dict(os.environ, {'GOD2_REGRESSION_EVIDENCE': tmp}), patch.object(evidence.subprocess, 'Popen', return_value=process):
                    code = evidence.run(['dotnet', 'test', 'sample.csproj', '--logger',
                                         'trx;LogFileName=result.trx', '--results-directory', str(results)])
                self.assertEqual(2, code)
                record = json.loads(next((root / 'invocations').glob('*/invocation.json')).read_text())
                self.assertEqual(0, record['process_exit_code'])
                self.assertEqual('failed', record['status'])

    def test_worker_cleanup_and_final_collection_fail_closed(self):
        for initial in (0, 7):
            for cleanup_failed, collection in ((True, True), (False, False), (False, RuntimeError('collector failed'))):
                with self.subTest(initial=initial, cleanup_failed=cleanup_failed, collection=collection), tempfile.TemporaryDirectory() as tmp:
                    admin = Mock(side_effect=RuntimeError('cleanup failed')) if cleanup_failed else Mock(return_value='0')
                    with patch.object(worker, 'collect', side_effect=collection if isinstance(collection, Exception) else None,
                                      return_value=collection):
                        code = worker.finish(initial, Path(tmp), admin, 'synthetic_user', '127.0.0.1')
                    self.assertEqual(7 if initial == 7 else 1, code)
                    admin.assert_called()

    def test_db_identity_mismatch_missing_rows_and_failed_check(self):
        targets = [{'hostname': 'fixture'}, {'hostname': 'fixture'}]
        for problem in ('identity', 'missing', 'value'):
            with self.subTest(problem=problem), tempfile.TemporaryDirectory() as tmp:
                def query(container, secret, sql):
                    if sql.startswith('SELECT @@hostname'):
                        return ('wrong' if problem == 'identity' else 'fixture') + '\t3306\troot@localhost\t2'
                    if problem == 'missing':
                        return ''
                    names = re.findall(r"SELECT\s+'([^']+)'", sql, re.I)
                    return '\n'.join(f'{n}\t{1 if problem == "value" and i == 0 else 0}\t0' for i, n in enumerate(names))
                output = Path(tmp) / 'db.json'
                with patch.object(db_state, 'inspect_targets', return_value=targets), patch.object(db_state, 'admin', side_effect=query):
                    if problem == 'value':
                        self.assertFalse(db_state.collect(output))
                    else:
                        with self.assertRaises(RuntimeError):
                            db_state.collect(output)
                self.assertFalse(json.loads(output.read_text())['passed'])

    def test_container_shared_binding_is_rejected(self):
        data = {'Name': '/god2-merchant-db-isolated', 'State': {'Running': True},
                'NetworkSettings': {'Ports': {'3306/tcp': [{'HostIp': '127.0.0.1', 'HostPort': '3308'}]}}}
        with patch.object(db_state, 'docker', return_value=Mock(returncode=0, stdout=json.dumps([data]))):
            with self.assertRaises(RuntimeError):
                db_state.inspect_targets()

    def test_supervisor_summary_failure_is_nonzero_and_recorded(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'docs/handoff').mkdir(parents=True)
            snapshot = {'head': 'a', 'branch': 'branch', 'files': []}
            process = Mock(stdout=io.StringIO('worker finished\n'))
            process.wait.return_value = 0
            actual_save = evidence.save
            def save(path, value):
                if path.name == 'summary.json':
                    raise OSError('simulated storage failure')
                actual_save(path, value)
            cwd = Path.cwd()
            try:
                with patch.object(evidence, 'ROOT', root), patch.object(evidence, 'manifest', return_value=snapshot), patch.object(evidence.subprocess, 'Popen', return_value=process), patch.object(evidence, 'save', side_effect=save):
                    self.assertEqual(2, evidence.supervise([]))
            finally:
                os.chdir(cwd)
            record = json.loads(next((root / 'docs/handoff').glob('*/run.json')).read_text())
            self.assertEqual('OSError', record['collection_error'])


if __name__ == '__main__':
    unittest.main()
