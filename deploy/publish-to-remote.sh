#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir" && pwd)
config_file="${PUBLISH_TARGET_CONFIG:-$script_dir/publish-to-remote.local}"

if [[ ! -f "$config_file" ]]; then
    cat >&2 <<EOF
Missing deploy target config: $config_file
Create it from deploy/publish-to-remote.local.example and set PUBLISH_HOST, PUBLISH_PATH, and optionally PUBLISH_USER, PUBLISH_PORT, PUBLISH_CONFIGURATION, and PUBLISH_SERVICE_NAME.
EOF
    exit 1
fi

# shellcheck disable=SC1090
source "$config_file"

: "${PUBLISH_HOST:?Set PUBLISH_HOST in $config_file}"
: "${PUBLISH_PATH:?Set PUBLISH_PATH in $config_file}"

publish_configuration="${PUBLISH_CONFIGURATION:-Production}"
project_path="$repo_root/Server/HomeCompanion.Local.Server.csproj"
publish_dir=$(mktemp -d -t homecompanion-publish.XXXXXX)
remote_config_dir="/etc/homecompanion"
service_name="${PUBLISH_SERVICE_NAME:-homecompanion}"

if [[ ! -f "$project_path" ]]; then
    cat >&2 <<EOF
Missing local server project: $project_path
Invoke this deploy script through the HomeCompanion.Local symlink so it resolves the local solution root.
EOF
    exit 1
fi

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

remote_config_path_quoted=$(printf '%q' "$remote_config_dir")

# Initial deployment if no config files exist yet; upgrade otherwise.
is_upgrade_deployment=$(
    ssh "${ssh_args[@]}" "$ssh_target" "
        if [[ -d $remote_config_path_quoted ]] && compgen -G \"$remote_config_path_quoted/*.json\" >/dev/null; then
            echo 1
        else
            echo 0
        fi
    "
)

remote_service_exists=$(
    ssh "${ssh_args[@]}" "$ssh_target" "
        if systemctl list-unit-files --type=service --all 2>/dev/null | grep -Fq \"$service_name.service\"; then
            echo 1
        else
            echo 0
        fi
    "
)

if [[ "$is_upgrade_deployment" == "1" ]]; then
    echo "Detected upgrade deployment (existing JSON config in $remote_config_dir)."
else
    echo "Detected initial deployment (no JSON config in $remote_config_dir yet)."
fi

if [[ "$remote_service_exists" == "1" ]]; then
    echo "Stopping $service_name.service on remote host..."
    ssh "${ssh_args[@]}" "$ssh_target" "systemctl stop $service_name"
else
    echo "Remote service $service_name.service not found; skipping stop/start/status."
fi

scp "${scp_args[@]}" "$publish_dir"/. "$ssh_target:$PUBLISH_PATH/"

shopt -s nullglob
config_files=("$repo_root/Config"/*.json)
shopt -u nullglob

if (( ${#config_files[@]} > 0 )); then
    ssh "${ssh_args[@]}" "$ssh_target" "mkdir -p -- $remote_config_path_quoted"

    remote_config_staging_dir="$PUBLISH_PATH/.homecompanion-config-staging"
    remote_config_staging_dir_quoted=$(printf '%q' "$remote_config_staging_dir")

    ssh "${ssh_args[@]}" "$ssh_target" "rm -rf -- $remote_config_staging_dir_quoted && mkdir -p -- $remote_config_staging_dir_quoted"
    scp "${scp_args[@]}" "${config_files[@]}" "$ssh_target:$remote_config_staging_dir/"

    ssh "${ssh_args[@]}" "$ssh_target" "
        shopt -s nullglob
        for source_file in $remote_config_staging_dir_quoted/*.json; do
            target_file=$remote_config_path_quoted/\$(basename \"\$source_file\")
            if [[ -e \"\$target_file\" ]]; then
                echo \"Preserving existing local config: \$target_file\"
            else
                install -m 0644 \"\$source_file\" \"\$target_file\"
                echo \"Installed missing local config: \$target_file\"
            fi
        done
        rm -rf -- $remote_config_staging_dir_quoted
    "
fi

if [[ "$remote_service_exists" == "1" ]]; then
    echo "Starting $service_name.service on remote host..."
    ssh "${ssh_args[@]}" "$ssh_target" "systemctl start $service_name"
    echo "Service status for $service_name.service:"
    ssh "${ssh_args[@]}" "$ssh_target" "systemctl status --no-pager $service_name"
fi
