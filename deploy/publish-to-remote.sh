#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/.." && pwd)
config_file="${PUBLISH_TARGET_CONFIG:-$script_dir/publish-to-remote.local}"

if [[ ! -f "$config_file" ]]; then
    cat >&2 <<EOF
Missing deploy target config: $config_file
Create it from deploy/publish-to-remote.local.example and set PUBLISH_HOST, PUBLISH_PATH, and optionally PUBLISH_USER, PUBLISH_PORT, and PUBLISH_CONFIGURATION.
EOF
    exit 1
fi

# shellcheck disable=SC1090
source "$config_file"

: "${PUBLISH_HOST:?Set PUBLISH_HOST in $config_file}"
: "${PUBLISH_PATH:?Set PUBLISH_PATH in $config_file}"

publish_configuration="${PUBLISH_CONFIGURATION:-Production}"
project_path="$repo_root/../Server/HomeCompanion.Local.Server.csproj"
publish_dir=$(mktemp -d -t homecompanion-publish.XXXXXX)

cleanup() {
    rm -rf "$publish_dir"
}

trap cleanup EXIT

dotnet_publish_args=(publish "$project_path" -c "$publish_configuration" -o "$publish_dir")
if declare -p DOTNET_PUBLISH_ARGS >/dev/null 2>&1; then
    dotnet_publish_args+=("${DOTNET_PUBLISH_ARGS[@]}")
fi

dotnet "${dotnet_publish_args[@]}"

ssh_target="$PUBLISH_HOST"
if [[ -n "${PUBLISH_USER:-}" ]]; then
    ssh_target="$PUBLISH_USER@$ssh_target"
fi

ssh_args=()
scp_args=(-r)
if [[ -n "${PUBLISH_PORT:-}" ]]; then
    ssh_args+=(-p "$PUBLISH_PORT")
    scp_args+=(-P "$PUBLISH_PORT")
fi

remote_path_quoted=$(printf '%q' "$PUBLISH_PATH")
ssh "${ssh_args[@]}" "$ssh_target" "mkdir -p -- $remote_path_quoted"

scp "${scp_args[@]}" "$publish_dir"/. "$ssh_target:$PUBLISH_PATH/"