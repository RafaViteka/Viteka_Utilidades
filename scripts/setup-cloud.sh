#!/usr/bin/env bash
set -euo pipefail
export DOTNET_ROOT=/workspace/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME=/workspace/.cache/dotnet
export NUGET_PACKAGES=/workspace/.cache/nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
if ! "$DOTNET_ROOT/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.0\.403 '; then
    curl --fail --show-error --location https://dot.net/v1/dotnet-install.sh --output /tmp/viteka-dotnet-install.sh
    bash /tmp/viteka-dotnet-install.sh --version 10.0.403 --azure-feed https://builds.dotnet.microsoft.com/dotnet --install-dir "$DOTNET_ROOT"
fi
cd /workspace/Viteka_Utilidades
export npm_config_cache=/workspace/.cache/npm
(cd frontend && npm ci --no-audit --no-fund && npm run build)
python scripts/copy-ui.py
python scripts/build-tenencia-catalog.py --check
dotnet restore Viteka.Utilidades.slnx --locked-mode
dotnet build Viteka.Utilidades.slnx --configuration Release --no-restore
dotnet run --project tests/Viteka.Core.Checks --configuration Release --no-build
python scripts/smoke-server.py
