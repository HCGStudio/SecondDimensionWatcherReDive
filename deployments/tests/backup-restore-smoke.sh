#!/usr/bin/env bash
set -Eeuo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
drill_root=$(mktemp -d "${TMPDIR:-/tmp}/sdw-backup-drill.XXXXXX")
container_name="sdw-backup-drill-$$"

cleanup() {
    podman stop "${container_name}" >/dev/null 2>&1 || true
    case "${drill_root}" in
        "${TMPDIR:-/tmp}"/sdw-backup-drill.*) rm -rf -- "${drill_root}" ;;
    esac
}
trap cleanup EXIT

mkdir -p \
    "${drill_root}/keys" \
    "${drill_root}/plugins" \
    "${drill_root}/backups" \
    "${drill_root}/restored"
install -m 0600 "${repo_root}/VERSION" "${drill_root}/appsettings.yml"
install -m 0600 "${repo_root}/VERSION" "${drill_root}/password.json"
app_version=$(tr -d '[:space:]' <"${repo_root}/VERSION")

podman run --rm --detach --name "${container_name}" \
    --env POSTGRES_PASSWORD=postgres \
    --env POSTGRES_USER=postgres \
    --env POSTGRES_DB=sdw_source \
    --publish 127.0.0.1::5432 \
    postgres:17-alpine >/dev/null

port=$(podman port "${container_name}" 5432/tcp | sed -E 's/.*:([0-9]+)$/\1/')
export PGHOST=127.0.0.1 PGPORT="${port}" PGUSER=postgres PGPASSWORD=postgres PGDATABASE=sdw_source
for _ in {1..30}; do
    pg_isready --quiet && break
    sleep 1
done
pg_isready --quiet
psql --no-psqlrc --set=ON_ERROR_STOP=1 --command \
    "CREATE TABLE drill_items (id integer PRIMARY KEY, value text NOT NULL); INSERT INTO drill_items VALUES (1, 'round-trip');" \
    >/dev/null

export ConnectionStrings__sdw="Host=127.0.0.1;Port=${port};User ID=postgres;Password=postgres;Database=sdw_source"
unset PGHOST PGPORT PGUSER PGPASSWORD PGDATABASE
archive=$("${repo_root}/deployments/sdw-backup" create \
    --output "${drill_root}/backups" \
    --config "${drill_root}/appsettings.yml" \
    --password-file "${drill_root}/password.json" \
    --key-ring "${drill_root}/keys" \
    --plugin-dir "${drill_root}/plugins" \
    --retention-days 7 \
    --app-version "${app_version}")
unset ConnectionStrings__sdw
export PGHOST=127.0.0.1 PGPORT="${port}" PGUSER=postgres PGPASSWORD=postgres PGDATABASE=sdw_source
"${repo_root}/deployments/sdw-backup" verify "${archive}"

cp -- "${archive}" "${drill_root}/corrupt.tar.gz"
archive_size=$(stat --format=%s "${drill_root}/corrupt.tar.gz")
printf CORRUPT | dd of="${drill_root}/corrupt.tar.gz" bs=1 \
    seek=$((archive_size / 2)) conv=notrunc status=none
if "${repo_root}/deployments/sdw-backup" verify "${drill_root}/corrupt.tar.gz" >/dev/null 2>&1; then
    printf 'corrupt archive unexpectedly verified\n' >&2
    exit 1
fi

createdb sdw_restore
export PGDATABASE=sdw_restore
"${repo_root}/deployments/sdw-backup" restore "${archive}" \
    --confirm-replace \
    --expected-version "${app_version}" \
    --config-destination "${drill_root}/restored/appsettings.yml" \
    --password-destination "${drill_root}/restored/password.json" \
    --key-ring-destination "${drill_root}/restored/keys" \
    --plugin-destination "${drill_root}/restored/plugins" \
    --safety-directory "${drill_root}/backups"

test "$(psql --no-psqlrc --tuples-only --no-align --command \
    'SELECT value FROM drill_items WHERE id = 1')" = round-trip
cmp "${drill_root}/password.json" "${drill_root}/restored/password.json"
printf 'backup restore smoke test passed\n'
