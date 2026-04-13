#!/bin/bash
set -e

CONFIG="/etc/sdw-redive/appsettings.yml"

# Create system user and group if they don't exist
if ! getent group sdw-redive >/dev/null 2>&1; then
    groupadd --system sdw-redive
fi

if ! getent passwd sdw-redive >/dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin \
        --gid sdw-redive --home-dir /var/lib/sdw-redive sdw-redive
fi

# Generate JwtSecret on first install (placeholder still present)
if grep -q '<Please fill this with a 32 length random string>' "$CONFIG" 2>/dev/null; then
    JWT_SECRET=$(tr -dc 'A-Za-z0-9' </dev/urandom | head -c 48)
    sed -i "s|<Please fill this with a 32 length random string>|${JWT_SECRET}|" "$CONFIG"
fi

# Ensure data directory ownership
chown -R sdw-redive:sdw-redive /var/lib/sdw-redive

# Reload systemd
systemctl daemon-reload
