#!/bin/bash
set -e

# Debian passes "upgrade" and RPM passes 1 for replacement upgrades. Preserve
# the administrator's enabled/running state in those cases; this hook owns only
# final package removal.
case "${1:-remove}" in
    upgrade|1) exit 0 ;;
esac

# Package-image roots do not necessarily run systemd. On a real host, stop the
# timer before its service and propagate every genuine systemctl failure.
if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
    # stop/disable are idempotent for inactive/disabled installed units. Calling
    # them directly keeps D-Bus and unit errors visible to the package manager.
    systemctl stop sdw-backup.timer sdw-backup.service sdw-redive.service
    systemctl disable sdw-backup.timer sdw-redive.service
fi
