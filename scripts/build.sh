#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
: "${VALHEIM_MANAGED_DIR:?Set VALHEIM_MANAGED_DIR to the Valheim managed assembly directory}"
: "${BEPINEX_CORE_DIR:?Set BEPINEX_CORE_DIR to the BepInEx core directory}"

dotnet build "$repo_root/src/RagnavikUI.csproj" \
  --configuration Release \
  --nologo \
  --property:ContinuousIntegrationBuild=true \
  --property:Deterministic=true \
  --property:ValheimManagedDir="$VALHEIM_MANAGED_DIR" \
  --property:BepInExCoreDir="$BEPINEX_CORE_DIR"
