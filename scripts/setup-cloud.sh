#!/usr/bin/env bash
set -euo pipefail

ZEUS_REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
export ZEUS_SDK_ROOT="${ZEUS_SDK_ROOT:-/workspace/.dotnet}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/workspace/.dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/workspace/.nuget/packages}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export ZEUS_GLOBAL_JSON="$ZEUS_REPO_ROOT/global.json"

python - <<'PY'
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import urllib.request

version = json.loads(Path(os.environ['ZEUS_GLOBAL_JSON']).read_text())['sdk']['version']
destination = Path(os.environ['ZEUS_SDK_ROOT'])
binary = destination / 'dotnet'
if binary.is_file():
    installed = subprocess.run([str(binary), '--list-sdks'], check=True, capture_output=True, text=True)
    if any(line.startswith(version + ' ') for line in installed.stdout.splitlines()):
        print(f'.NET SDK {version} already installed.')
        raise SystemExit(0)

if os.uname().sysname != 'Linux' or os.uname().machine != 'x86_64':
    raise SystemExit('This cloud setup script supports Linux x64. Install the pinned SDK for your platform.')

metadata_url = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
with urllib.request.urlopen(metadata_url, timeout=60) as response:
    metadata = json.load(response)

artifact = None
for release in metadata['releases']:
    for sdk in [release.get('sdk', {})] + release.get('sdks', []):
        if sdk.get('version') == version:
            artifact = next((entry for entry in sdk['files']
                             if entry['rid'] == 'linux-x64' and entry['name'].endswith('.tar.gz')), None)
    if artifact:
        break
if not artifact:
    raise SystemExit('Pinned SDK missing from official Microsoft release metadata.')

print(f'Downloading official .NET SDK {version}...')
with tempfile.TemporaryDirectory(prefix='zeus-sdk-') as temporary:
    archive = Path(temporary) / 'sdk.tar.gz'
    digest = hashlib.sha512()
    with urllib.request.urlopen(artifact['url'], timeout=120) as response, archive.open('wb') as output:
        while chunk := response.read(1024 * 1024):
            output.write(chunk)
            digest.update(chunk)
    if digest.hexdigest().lower() != artifact['hash'].lower():
        raise SystemExit('Official SHA-512 mismatch. Installation stopped.')
    destination.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive) as package:
        package.extractall(destination, filter='data')
    print(f'Official SHA-512 verified; SDK installed in {destination}.')
PY

export PATH="$ZEUS_SDK_ROOT:$PATH"
cd "$ZEUS_REPO_ROOT"
dotnet --version
if [[ -f Zeus.slnx ]]; then
  dotnet restore Zeus.slnx
fi
