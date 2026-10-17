"""Local evidence only: no environment dumps, source copies, or DB writes."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SUITES = ('Core', 'Application', 'Session', 'Protocol', 'Network', 'Persistence.Integration')


def utc():
    return datetime.now(timezone.utc).isoformat()


def save(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT).decode().strip()


def manifest():
    """Conservative project-directory closure includes untracked/ignored inputs."""
    projects = {ROOT / f'tests/God2.ServerV2.{s}.Tests/God2.ServerV2.{s}.Tests.csproj'
                for s in SUITES if s != 'Persistence.Integration'}
    projects.add(ROOT / 'tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj')
    pending = list(projects)
    while pending:
        p = pending.pop()
        for ref in ET.parse(p).iter('ProjectReference'):
            target = (p.parent / ref.attrib['Include'].replace('\\', '/')).resolve()
            if not target.is_relative_to(ROOT):
                raise RuntimeError('External project input requires an explicit manifest scope.')
            if target not in projects:
                projects.add(target)
                pending.append(target)
    paths = set()
    excluded = []
    for project in projects:
        for p in project.parent.rglob('*'):
            if not p.is_file() or any(x in ('bin', 'obj', '.git', '__pycache__', 'TestResults') for x in p.parts):
                continue
            rel = p.relative_to(ROOT).as_posix()
            if p.suffix.lower() in ('.env', '.pem', '.key', '.pfx', '.p12') or re.search(r'(secret|credential|appsettings)', p.name, re.I):
                if p.suffix.lower() in ('.cs', '.csproj', '.props', '.targets'):
                    raise RuntimeError('Source cannot be excluded as a secret: ' + rel)
                excluded.append(rel)
                continue
            paths.add(p)
    # Test-read SQL, and all Automation source (conservative runner/helper superset).
    paths.update((ROOT / 'database/schema').glob('*.sql'))
    for p in (ROOT / 'Automation').rglob('*'):
        if p.is_file() and p.suffix in ('.sh', '.py', '.sql') and '__pycache__' not in p.parts:
            paths.add(p)
    # Build configuration at each project ancestor, including repo root.
    for project in projects:
        for parent in (project.parent, *project.parent.parents):
            if not parent.is_relative_to(ROOT):
                break
            for pattern in ('Directory.*.props', 'Directory.*.targets', 'global.json', 'NuGet.Config', 'nuget.config'):
                paths.update(parent.glob(pattern))
    tracked = set(git('ls-files', '-z').split('\0'))
    files = []
    for p in sorted(paths):
        if p.is_symlink():
            raise RuntimeError('Manifest input symlink requires explicit review: ' + str(p))
        rel = p.relative_to(ROOT).as_posix()
        files.append({'path': rel, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest(),
                      'tracked': rel in tracked})
    return {'utc': utc(), 'branch': git('branch', '--show-current'), 'head': git('rev-parse', 'HEAD'),
            'tracked_staged': git('diff', '--cached', '--name-status'),
            'tracked_unstaged': git('diff', '--name-status'),
            'projects': sorted(p.relative_to(ROOT).as_posix() for p in projects),
            'scope': 'Six test projects and transitive project directories; ancestor build configuration; database/schema SQL; Automation sh/py/sql superset. Includes untracked and ignored source. Excludes bin/obj/TestResults/__pycache__, evidence, secret configuration. No source contents copied.',
            'excluded_secret_configuration': sorted(excluded), 'files': files}


def changes(before, after):
    old = {x['path']: x['sha256'] for x in before['files']}
    new = {x['path']: x['sha256'] for x in after['files']}
    return sorted(p for p in old.keys() | new.keys() if old.get(p) != new.get(p))


def redact(value, environment=None):
    for key, secret in (environment if environment is not None else os.environ).items():
        if key.startswith('GOD2_') and re.search('PASSWORD|SECRET|TOKEN', key) and secret:
            value = value.replace(secret, '[REDACTED]')
    return value


def run(command, env=None):
    """Only controlled runner/test arguments may be persisted; never env values."""
    base = os.environ.get('GOD2_REGRESSION_EVIDENCE')
    if not base:
        return subprocess.call(command, env=env)
    if command[:2] != ['dotnet', 'test'] and not (
            command[0] == 'bash' and len(command) == 2 and
            re.fullmatch(r'Automation/test-v2-[a-z0-9-]+(?:isolated|no-progress)\.sh', command[1])):
        raise ValueError('Unapproved evidence command')
    if any(re.search(r'password|connectionstring|secret|token|https?://', x, re.I) for x in command):
        raise ValueError('Sensitive command argument rejected')
    folder = Path(tempfile.mkdtemp(prefix='invocation-', dir=Path(base) / 'invocations'))
    record = {'started_utc': utc(), 'argv': command, 'exit_code': None, 'status': 'running'}
    save(folder / 'invocation.json', record)
    code = 1
    try:
        with (folder / 'output.log').open('w') as log:
            proc = subprocess.Popen(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
            for line in proc.stdout:
                line = redact(line, env)
                log.write(line)
                log.flush()
                print(line, end='', flush=True)
            code = proc.wait()
        return code
    finally:
        record.update(ended_utc=utc(), exit_code=code, status='passed' if code == 0 else 'failed')
        record['trx'] = []
        if command[:2] == ['dotnet', 'test'] and '--results-directory' in command:
            result_dir = Path(command[command.index('--results-directory') + 1])
            logger = command[command.index('--logger') + 1] if '--logger' in command else ''
            filename = re.search(r'(?:^|;)LogFileName=([^;]+)', logger)
            candidates = [result_dir / filename[1]] if filename else []
            record['trx_expected'] = str(candidates[0]) if candidates else None
            for p in candidates:
                if not p.is_file():
                    continue
                # The original remains untouched. Copies are redacted, including failures.
                dest = folder / p.name
                dest.write_text(redact(p.read_text(), env))
                entry = {'path': str(p), 'copy': dest.name}
                try:
                    counters = ET.parse(dest).getroot().find('.//{*}Counters')
                    entry['counters'] = counters.attrib if counters is not None else None
                except ET.ParseError:
                    entry['parse_error'] = True
                record['trx'].append(entry)
        if command[0] == 'bash':
            output = (folder / 'output.log').read_text()
            markers = ('PASS: exit cleanup verification completed',
                       'PASS: position fixture, synthetic map and user removed',
                       'remaining_fixture_accounts_and_users=0',
                       'PASS: all atomic fault users and triggers absent')
            record['cleanup_markers'] = {m: output.count(m) for m in markers if m in output}
        save(folder / 'invocation.json', record)


def supervise(args):
    if args not in ([], ['--persistence']):
        raise ValueError('Only no argument or --persistence is supported')
    os.chdir(ROOT)
    folder = Path(tempfile.mkdtemp(prefix='v2-regression-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ-'), dir=ROOT / 'docs/handoff'))
    (folder / 'invocations').mkdir()
    print('regression_evidence=' + str(folder), flush=True)
    before = manifest()
    save(folder / 'source-before.json', before)
    record = {'started_utc': utc(), 'argv': ['bash', 'Automation/test-v2-all-isolated.sh', *args],
              'test_exit_code': None, 'exit_code': None}
    save(folder / 'run.json', record)
    code = 1
    try:
        env = {k: v for k, v in os.environ.items() if not k.startswith('GOD2_') and k not in
               ('DOCKER_HOST', 'DOCKER_CONTEXT', 'DOCKER_TLS', 'DOCKER_TLS_VERIFY', 'DOCKER_CERT_PATH')}
        env['GOD2_REGRESSION_EVIDENCE'] = str(folder)
        with (folder / 'entry.log').open('w') as log:
            proc = subprocess.Popen([sys.executable, 'Automation/run_v2_isolated.py', *args], env=env,
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
            for line in proc.stdout:
                log.write(line)
                log.flush()
                print(line, end='', flush=True)
            code = proc.wait()
    finally:
        record['test_exit_code'] = code
        try:
            after = manifest()
            save(folder / 'source-after.json', after)
            changed = changes(before, after)
            stable = not changed and before['head'] == after['head'] and before['branch'] == after['branch']
            record.update(source_stable=stable, changed_paths=changed)
            if not stable and code == 0:
                code = 2
        except Exception as error:
            record.update(source_stable=False, finalization_error=type(error).__name__)
            if code == 0:
                code = 2
        record.update(exit_code=code, ended_utc=utc(),
                      counting='Each invocation is retained separately. Fixture counts include repeated prerequisite executions; no unique combined total.')
        invocations = [json.loads(p.read_text()) for p in (folder / 'invocations').glob('*/invocation.json')]
        save(folder / 'summary.json', {'run': record, 'invocations': sorted(invocations, key=lambda x: x['started_utc'])})
        save(folder / 'run.json', record)
        print('regression_exit_code=' + str(code), flush=True)
    return code


if __name__ == '__main__':
    if sys.argv[1:2] == ['run']:
        sys.exit(run(sys.argv[2:]))
    if sys.argv[1:2] == ['entry']:
        sys.exit(supervise(sys.argv[2:]))
    raise SystemExit('Use entry or run')
