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

# appsettings.yml contains database, JWT and upstream credentials. Protect an
# existing conffile before reading, migrating or adding generated secrets.
if [ -f "$CONFIG" ]; then
    chown root:sdw-redive "$CONFIG"
    chmod 0640 "$CONFIG"
fi
if [ -f /etc/sdw-redive/backup.env ]; then
    chown root:sdw-redive /etc/sdw-redive/backup.env
    chmod 0640 /etc/sdw-redive/backup.env
fi

# Add sdw-redive to valkey group if it exists (for Unix socket access)
if getent group valkey >/dev/null 2>&1; then
    usermod -aG valkey sdw-redive
fi

# Generate JwtSecret on first install (placeholder still present)
if grep -q '<Please fill this with a 32 length random string>' "$CONFIG" 2>/dev/null; then
    JWT_SECRET=$(tr -dc 'A-Za-z0-9' </dev/urandom | head -c 48)
    sed -i "s|<Please fill this with a 32 length random string>|${JWT_SECRET}|" "$CONFIG"
fi

# Migrate legacy Inference:* config to AI:* structure (upgrade from <2.2)
MIGRATE="/usr/lib/sdw-redive/migrate-config.sh"
if [ -x "$MIGRATE" ]; then
    "$MIGRATE" "$CONFIG" || true
fi

# Ensure data directory ownership
chown -R sdw-redive:sdw-redive /var/lib/sdw-redive

# Data Protection keys and the password hash are service-owned secrets. The
# private directory also protects keys created by future application runs.
install -d -m 0700 -o sdw-redive -g sdw-redive \
    /var/lib/sdw-redive/data-protection-keys \
    /var/lib/sdw-redive/backups
if [ -f /var/lib/sdw-redive/password.json ]; then
    chown sdw-redive:sdw-redive /var/lib/sdw-redive/password.json
    chmod 0600 /var/lib/sdw-redive/password.json
fi
find /var/lib/sdw-redive/data-protection-keys -type f \
    -exec chown sdw-redive:sdw-redive {} + \
    -exec chmod 0600 {} +

# Package-image roots do not necessarily run systemd. On a real host, malformed
# units and daemon failures must still fail package installation.
if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
    systemctl daemon-reload
fi
