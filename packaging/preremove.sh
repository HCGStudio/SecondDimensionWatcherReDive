#!/bin/bash
set -e

# Stop and disable the service before removal
if systemctl is-active --quiet sdw-redive; then
    systemctl stop sdw-redive
fi

if systemctl is-enabled --quiet sdw-redive 2>/dev/null; then
    systemctl disable sdw-redive
fi
