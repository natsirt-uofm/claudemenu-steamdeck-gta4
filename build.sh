#!/usr/bin/env bash
# Linux / Steam Deck build (needs mono's `mcs`).
#   ./build.sh /path/to/GTA\ IV        -> builds scripts/ClaudeMenu.net.dll against the real ScriptHookDotNet.dll
#   ./build.sh --check                 -> compile check only, against the bundled API stub (no game needed)
set -euo pipefail
cd "$(dirname "$0")"
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT

if [[ "${1:-}" == "--check" ]]; then
  mcs -langversion:4 -target:library -out:"$out/ScriptHookDotNet.dll" tools/shdn-stub/ScriptHookDotNetStub.cs
  mcs -langversion:4 -warnaserror -target:library -r:"$out/ScriptHookDotNet.dll" -out:"$out/ClaudeMenu.net.dll" scripts/ClaudeMenu.cs
  echo "ClaudeMenu.cs compiles as C# 4 (stub check only)."
  exit 0
fi

gta="${1:?usage: ./build.sh <GTA IV folder containing ScriptHookDotNet.dll> | --check}"
mcs -langversion:4 -target:library -optimize+ -r:"$gta/ScriptHookDotNet.dll" -out:scripts/ClaudeMenu.net.dll scripts/ClaudeMenu.cs
echo "Built scripts/ClaudeMenu.net.dll"
