#!/bin/bash
# Build a FreeBSD .pkg from a staging directory (runs on Linux).
#
# FreeBSD pkg format: tar+xz archive with +COMPACT_MANIFEST and +MANIFEST
# as the first entries, followed by files relative to prefix.
#
# Usage: build-pkg.sh <staging_dir> <output_file> <version> <arch>
#   staging_dir  — root with files relative to prefix (e.g., lib/sdw-redive/...)
#   output_file  — path for the resulting .pkg
#   version      — package version (e.g., 2.0.0.42)
#   arch         — amd64 or aarch64

set -euo pipefail

STAGING="$1"
OUTPUT="$2"
VERSION="$3"
ARCH="$4"

NAME="sdw-redive"
PREFIX="/usr/local"

# --- Build files JSON: { "/usr/local/lib/...": "1$sha256", ... } ---
FILES="{"
FIRST=true
while IFS= read -r -d '' file; do
    rel="${file#${STAGING}/}"
    abs_path="${PREFIX}/${rel}"
    hash=$(sha256sum "$file" | cut -d' ' -f1)
    if [ "$FIRST" = true ]; then FIRST=false; else FILES+=","; fi
    FILES+="\"${abs_path}\":\"1\$${hash}\""
done < <(find "$STAGING" -type f -print0 | sort -z)
FILES+="}"

# --- Build directories JSON ---
DIRS="{"
FIRST=true
while IFS= read -r -d '' dir; do
    rel="${dir#${STAGING}/}"
    [ -z "$rel" ] && continue
    abs_path="${PREFIX}/${rel}"
    if [ "$FIRST" = true ]; then FIRST=false; else DIRS+=","; fi
    DIRS+="\"${abs_path}\":\"y\""
done < <(find "$STAGING" -mindepth 1 -type d -print0 | sort -z)
DIRS+="}"

# --- Scripts ---
read -r -d '' POST_INSTALL << 'SCRIPT' || true
#!/bin/sh
pw groupadd sdw-redive 2>/dev/null || true
pw useradd sdw-redive -g sdw-redive -d /var/db/sdw-redive -s /usr/sbin/nologin -c "SDW Re:Dive" 2>/dev/null || true
mkdir -p /var/db/sdw-redive/downloads
chown -R sdw-redive:sdw-redive /var/db/sdw-redive
if grep -q '<Please fill this with a 32 length random string>' /usr/local/etc/sdw-redive/appsettings.yml 2>/dev/null; then
    JWT_SECRET=$(openssl rand -base64 36)
    sed -i '' "s|<Please fill this with a 32 length random string>|${JWT_SECRET}|" /usr/local/etc/sdw-redive/appsettings.yml
fi
SCRIPT

read -r -d '' PRE_DEINSTALL << 'SCRIPT' || true
#!/bin/sh
service sdw_redive stop 2>/dev/null || true
SCRIPT

# --- Escape scripts for JSON ---
escape_json() {
    printf '%s' "$1" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()), end="")'
}

SCRIPTS_JSON="{\"post-install\":$(escape_json "$POST_INSTALL"),\"pre-deinstall\":$(escape_json "$PRE_DEINSTALL")}"

# --- +COMPACT_MANIFEST (no files/dirs) ---
COMPACT=$(cat <<EOF
{"name":"${NAME}","version":"${VERSION}","origin":"www/${NAME}","comment":"Anime download management system","desc":"SecondDimensionWatcher Re:Dive","maintainer":"mahoshojoHCG","www":"https://github.com/mahoshojoHCG/SecondDimensionWatcherReDive","prefix":"${PREFIX}","arch":"FreeBSD:*:${ARCH}","categories":["www"],"scripts":${SCRIPTS_JSON}}
EOF
)

# --- +MANIFEST (with files and dirs) ---
MANIFEST=$(cat <<EOF
{"name":"${NAME}","version":"${VERSION}","origin":"www/${NAME}","comment":"Anime download management system","desc":"SecondDimensionWatcher Re:Dive","maintainer":"mahoshojoHCG","www":"https://github.com/mahoshojoHCG/SecondDimensionWatcherReDive","prefix":"${PREFIX}","arch":"FreeBSD:*:${ARCH}","categories":["www"],"scripts":${SCRIPTS_JSON},"files":${FILES},"directories":${DIRS}}
EOF
)

# --- Write metadata into staging dir ---
printf '%s' "$COMPACT" > "${STAGING}/+COMPACT_MANIFEST"
printf '%s' "$MANIFEST" > "${STAGING}/+MANIFEST"

# --- Create pkg: metadata first, then content files ---
cd "$STAGING"
{
    echo "+COMPACT_MANIFEST"
    echo "+MANIFEST"
    find . -not -name '+*' -not -path '.' \( -type f -o -type l \) | sed 's|^\./||' | sort
} | tar -cJf "$OUTPUT" --no-recursion -T -

# --- Clean up metadata from staging ---
rm -f "${STAGING}/+COMPACT_MANIFEST" "${STAGING}/+MANIFEST"

echo "Created: $OUTPUT"
