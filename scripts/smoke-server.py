"""Exercise a private real demo host, including enrollment. Never connects to Oracle."""
import json
import os
from pathlib import Path
import socket
import sys
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
with socket.socket() as probe:
    probe.bind(('127.0.0.1', 5187))
DOTNET = os.environ.get('VITEKA_DOTNET', '/workspace/.dotnet/dotnet')
DLL = ROOT / 'src/Viteka.Server/bin/Release/net10.0/Viteka.Server.dll'
client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
if sys.platform != 'win32':
    rejected = subprocess.run([DOTNET, str(DLL), '--service', '--admin-list-devices'], capture_output=True, text=True)
    assert rejected.returncode == 2
    assert 'requiere Windows' in rejected.stderr



def request(path, method='GET', expected=200, payload=None, token=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request('http://127.0.0.1:5187' + path, method=method,
                                 data=json.dumps(payload or {}).encode() if method == 'POST' else None,
                                 headers=headers)
    try:
        response = client.open(req, timeout=3)
    except urllib.error.HTTPError as exc:
        response = exc
    with response:
        body = response.read()
        assert response.code == expected, (path, response.code, expected)
        assert response.headers.get('Cache-Control') == 'no-store'
        return json.loads(body) if body else None


with tempfile.TemporaryDirectory(prefix='viteka-http-') as temporary, tempfile.TemporaryFile(mode='w+') as log:
    env = os.environ.copy()
    env['VITEKA_SERVER_STATE_DIRECTORY'] = str(Path(temporary) / 'registry')
    codefile = Path(temporary) / 'enrollment.txt'
    # Codes are written privately to disk and consumed in memory, never printed.
    subprocess.run([DOTNET, str(DLL), '--admin-issue-code', str(codefile)], cwd=ROOT, env=env,
                   stdout=subprocess.DEVNULL, check=True)
    code = codefile.read_text()
    codefile.unlink()
    process = subprocess.Popen([DOTNET, str(DLL)], cwd=ROOT, env=env, stdout=log, stderr=log)
    try:
        deadline = time.monotonic() + 15
        while True:
            if process.poll() is not None:
                log.seek(0)
                raise RuntimeError('Host exited: ' + log.read())
            try:
                health = request('/health')
                break
            except (OSError, urllib.error.URLError):
                if time.monotonic() > deadline:
                    raise TimeoutError('Host did not become ready')
                time.sleep(.15)
        assert health == {'status': 'ok', 'mode': 'demo'}
        request('/api/status', expected=401)
        request('/api/utilities', expected=401, token='0' * 64)
        request('/api/utilities/stock/execute', 'POST', 401)
        request('/pairing/redeem', 'POST', 401, {'code': '0' * 32, 'deviceName': 'TEST-PC'})
        credential = request('/pairing/redeem', 'POST', payload={'code': code, 'deviceName': 'TEST-PC'})
        token = credential['token']
        assert len(token) == 64
        request('/pairing/redeem', 'POST', 401, {'code': code, 'deviceName': 'OTHER-PC'})
        status = request('/api/status', token=token)
        assert status['databaseConfigured'] is False
        assert status['databaseStatus'] == 'not_configured'
        catalog = request('/api/utilities', token=token)
        assert {x['id'] for x in catalog} == {'stock', 'expiry', 'purchases'}
        assert all(x['requiresDatabase'] for x in catalog)
        for utility in catalog:
            result = request('/api/utilities/' + utility['id'] + '/execute', 'POST', 503, token=token)
            assert result['code'] == 'database_not_configured'
        request('/api/utilities/unknown/execute', 'POST', 404, token=token)
        request('/api/utilities/stock/execute', expected=405, token=token)
        request('/api/sql', 'POST', 404, token=token)
        for _ in range(2):
            request('/pairing/redeem', 'POST', 401, {'code': code, 'deviceName': 'TEST-PC'})
        request('/pairing/redeem', 'POST', 429, {'code': code, 'deviceName': 'TEST-PC'})
        subprocess.run([DOTNET, str(DLL), '--admin-revoke-device', credential['deviceId']], cwd=ROOT,
                       env=env, stdout=subprocess.DEVNULL, check=True)
        request('/api/status', expected=401, token=token)
        print('PASS: health; API authorization; one-use pairing; rate limit; revocation; catalog; 3 blocked executions; no arbitrary SQL endpoint.')
    finally:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
