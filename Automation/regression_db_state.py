"""Only the two named local isolated containers; never connects to shared ports."""
import json
import os
import re
from pathlib import Path
import subprocess
from regression_evidence import ROOT, save, utc, require

TARGETS = ((3310, 'god2-merchant-db-isolated', 'merchant-root-password'),
           (3311, 'god2-regression-db-isolated', 'regression-root-password'))


def docker(*args, **kwargs):
    env = {k: v for k, v in os.environ.items() if k not in
           ('DOCKER_HOST', 'DOCKER_CONTEXT', 'DOCKER_TLS', 'DOCKER_TLS_VERIFY', 'DOCKER_CERT_PATH')}
    return subprocess.run(['sudo', 'docker', '--host=unix:///var/run/docker.sock', *args],
                          env=env, capture_output=True, text=True, **kwargs)


def inspect_targets():
    records = []
    for port, container, _ in TARGETS:
        result = docker('inspect', container)
        if result.returncode:
            raise RuntimeError('Isolated container unavailable: ' + container)
        data = json.loads(result.stdout)[0]
        # Only selected non-secret identity fields are ever saved.
        require(data['Name'] == '/' + container and data['State']['Running'], 'Isolated regression safety check failed')
        require(data['NetworkSettings']['Ports']['3306/tcp'] == [
            {'HostIp': '127.0.0.1', 'HostPort': str(port)}], 'Isolated regression safety check failed')
        if port == 3311:
            require(data['Config']['Labels']['god2.purpose'] == 'isolated-regression-baseline', 'Isolated regression safety check failed')
        records.append({'container': container, 'container_id': data['Id'],
                        'hostname': data['Config']['Hostname'], 'host': '127.0.0.1', 'port': port})
    return records


def admin(container, secret, sql):
    # No SQL, stderr or credential values are included in exceptions.
    require((container, secret) in [(c, s) for _, c, s in TARGETS], 'Isolated regression safety check failed')
    command = ('set -eu; password="$(cat /run/secrets/' + secret + ')"; '
               'exec mariadb --no-defaults -uroot --password="$password" '
               '--batch --raw --skip-column-names')
    result = docker('exec', '-i', container, 'sh', '-lc', command, input=sql)
    if result.returncode:
        raise RuntimeError('Isolated read-only check failed: ' + container)
    return result.stdout.strip()


def collect(output):
    document = {'utc': utc(), 'targets': inspect_targets(), 'checks': [], 'passed': False}
    save(output, document)
    try:
        for target, (port, container, secret) in zip(document['targets'], TARGETS):
            identity = admin(container, secret, "SELECT @@hostname,@@port,CURRENT_USER(),(SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME IN ('god2_game','god2_player')); ")
            host, internal_port, principal, schemas = identity.split('\t')
            require(host == target['hostname'] and internal_port == '3306' and schemas == '2', 'Isolated regression safety check failed')
            require(principal.startswith('root@'), 'Isolated regression safety check failed')
            target['db_identity'] = {'hostname': host, 'internal_port': 3306, 'principal': principal,
                                     'schemas': ['god2_game', 'god2_player']}
            sql_path = ROOT / f'Automation/regression-db-{port}.sql'
            sql = sql_path.read_text()
            expected_names = re.findall(r"SELECT\s+'([^']+)'", sql, re.I)
            values = [line.split('\t') for line in admin(container, secret, sql).splitlines()]
            require(bool(expected_names) and len(values) == len(expected_names),
                    'Missing isolated named check results')
            require([row[0] for row in values] == expected_names and
                    len(set(expected_names)) == len(expected_names), 'Isolated check names differ')
            for name, actual, expected in values:
                document['checks'].append({'name': name, 'target': f'{container}/127.0.0.1:{port}',
                                           'sql_file': str(sql_path.relative_to(ROOT)),
                                           'actual': int(actual), 'expected': int(expected),
                                           'condition': 'actual == expected', 'passed': int(actual) == int(expected)})
        document['passed'] = bool(document['checks']) and all(c['passed'] for c in document['checks'])
        return document['passed']
    finally:
        save(output, document)
