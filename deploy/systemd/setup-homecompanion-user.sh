#!/usr/bin/env bash
set -euo pipefail

# Creates an unprivileged runtime user and required directories for HomeCompanion.
if ! id -u homecompanion >/dev/null 2>&1; then
    useradd --system --home /var/lib/homecompanion --shell /usr/sbin/nologin homecompanion
fi

install -d -o homecompanion -g homecompanion -m 0750 /var/lib/homecompanion
install -d -o homecompanion -g homecompanion -m 0750 /var/log/homecompanion
install -d -o root -g root -m 0755 /etc/homecompanion
