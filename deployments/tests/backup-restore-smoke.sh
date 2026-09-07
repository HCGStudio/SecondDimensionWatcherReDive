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
    "${drill_root}/plugins/example" \
    "${drill_root}/backups" \
    "${drill_root}/restored"
install -m 0600 "${repo_root}/VERSION" "${drill_root}/appsettings.yml"
install -m 0600 "${repo_root}/VERSION" "${drill_root}/password.json"
printf '<key id="drill"/>\n' >"${drill_root}/keys/key.xml"
printf '{"name":"example","version":"1"}\n' >"${drill_root}/plugins/example/manifest.json"
app_version=$(tr -d '[:space:]' <"${repo_root}/VERSION")

# EXIT, rather than ERR alone, must notify explicit die/exit paths without
# exposing the configured URL or any environment secret in the diagnostic.
mkdir "${drill_root}/webhook-bin"
cat >"${drill_root}/webhook-bin/curl" <<'EOF'
#!/usr/bin/env bash
while [[ $# -gt 0 ]]; do
    if [[ "$1" == --data ]]; then
        printf '%s\n' "$2" >"${WEBHOOK_CAPTURE}"
        exit 0
    fi
    shift
done
exit 2
EOF
chmod +x "${drill_root}/webhook-bin/curl"
webhook_log="${drill_root}/webhook.log"
if WEBHOOK_CAPTURE="${drill_root}/webhook-payload" \
    SDW_BACKUP_FAILURE_WEBHOOK='https://secret.invalid/opaque-token' \
    PATH="${drill_root}/webhook-bin:/usr/bin:/bin" \
    "${repo_root}/deployments/sdw-backup" invalid-command >"${webhook_log}" 2>&1; then
    printf 'invalid command unexpectedly succeeded\n' >&2
    exit 1
fi
test "$(cat "${drill_root}/webhook-payload")" = '{"event":"sdw_backup_failed"}'
! grep -q 'opaque-token' "${webhook_log}"
if SDW_BACKUP_FAILURE_WEBHOOK='https://secret.invalid/opaque-token' PATH="${drill_root}/empty-path" \
    /bin/bash "${repo_root}/deployments/sdw-backup" invalid-command >/dev/null 2>&1; then
    printf 'configured webhook unexpectedly accepted a missing curl\n' >&2
    exit 1
fi

podman run --rm --detach --name "${container_name}" \
    --env POSTGRES_PASSWORD=postgres \
    --env POSTGRES_USER=postgres \
    --env POSTGRES_DB=postgres \
    --publish 127.0.0.1::5432 \
    postgres:17-alpine >/dev/null

port=$(podman port "${container_name}" 5432/tcp | sed -E 's/.*:([0-9]+)$/\1/')
export PGHOST=127.0.0.1 PGPORT="${port}" PGUSER=postgres PGPASSWORD=postgres PGDATABASE=postgres
for _ in {1..30}; do
    pg_isready --quiet && break
    sleep 1
done
pg_isready --quiet
psql --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL' >/dev/null
CREATE ROLE sdw_app LOGIN PASSWORD 'app-password';
CREATE ROLE sdw_restore_admin LOGIN CREATEDB PASSWORD 'restore-password';
GRANT sdw_app TO sdw_restore_admin;
CREATE DATABASE sdw_source OWNER sdw_app;
SQL

export PGUSER=sdw_app PGPASSWORD=app-password PGDATABASE=sdw_source
psql --no-psqlrc --set=ON_ERROR_STOP=1 --command \
    "CREATE TABLE drill_items (id integer PRIMARY KEY, value text NOT NULL); INSERT INTO drill_items VALUES (1, 'round-trip');" \
    >/dev/null

# Simulate the application startup hook already owning SDWMIGR1. The explicit
# flag must avoid a child-process self-deadlock while the dump remains valid.
coproc HELD_LEASE {
    psql --no-psqlrc --quiet --tuples-only --no-align --set=ON_ERROR_STOP=1
}
printf 'SELECT pg_advisory_lock(6000016593017852465);\n\\echo HELD\n' >&"${HELD_LEASE[1]}"
while IFS= read -r lease_line <&"${HELD_LEASE[0]}"; do
    [[ "${lease_line}" == HELD ]] && break
done
archive=$("${repo_root}/deployments/sdw-backup" create --migration-lock-held \
    --output "${drill_root}/backups" \
    --config "${drill_root}/appsettings.yml" \
    --password-file "${drill_root}/password.json" \
    --key-ring "${drill_root}/keys" \
    --plugin-dir "${drill_root}/plugins" \
    --retention-days 7 \
    --app-version "${app_version}")
printf '\\q\n' >&"${HELD_LEASE[1]}"
wait "${HELD_LEASE_PID}"

# Exercise normal lease acquisition too; use this archive for the restore drill.
archive=$("${repo_root}/deployments/sdw-backup" create \
    --output "${drill_root}/backups" \
    --config "${drill_root}/appsettings.yml" \
    --password-file "${drill_root}/password.json" \
    --key-ring "${drill_root}/keys" \
    --plugin-dir "${drill_root}/plugins" \
    --retention-days 7 \
    --app-version "${app_version}")

"${repo_root}/deployments/sdw-backup" verify "${archive}"
cp -- "${archive}" "${drill_root}/corrupt.tar.gz"
archive_size=$(stat --format=%s "${drill_root}/corrupt.tar.gz")
printf CORRUPT | dd of="${drill_root}/corrupt.tar.gz" bs=1 \
    seek=$((archive_size / 2)) conv=notrunc status=none
if "${repo_root}/deployments/sdw-backup" verify "${drill_root}/corrupt.tar.gz" >/dev/null 2>&1; then
    printf 'corrupt archive unexpectedly verified\n' >&2
    exit 1
fi

export PGUSER=postgres PGPASSWORD=postgres PGDATABASE=postgres
createdb --template=sdw_source --owner=sdw_app sdw_restore
export PGUSER=sdw_restore_admin PGPASSWORD=restore-password PGDATABASE=sdw_restore
export PGMAINTENANCEDATABASE=postgres
psql --no-psqlrc --set=ON_ERROR_STOP=1 --command \
    "CREATE TABLE restore_guard (id integer PRIMARY KEY); INSERT INTO restore_guard VALUES (99);" \
    >/dev/null
available_kib=$(podman exec "${container_name}" df -Pk /var/lib/postgresql/data | awk 'NR == 2 {print $4}')
available_bytes=$((available_kib * 1024))
restore_options=(
    --confirm-replace
    --expected-version "${app_version}"
    --expected-schema uninitialized
    --postgres-available-bytes "${available_bytes}"
    --config-destination "${drill_root}/restored/appsettings.yml"
    --password-destination "${drill_root}/restored/password.json"
    --key-ring-destination "${drill_root}/restored/keys"
    --plugin-destination "${drill_root}/restored/plugins"
    --safety-directory "${drill_root}/backups")

if "${repo_root}/deployments/sdw-backup" restore "${archive}" \
    "${restore_options[@]}" --expected-schema incompatible >/dev/null 2>&1; then
    printf 'schema mismatch unexpectedly restored\n' >&2
    exit 1
fi
test "$(psql --no-psqlrc -Atc 'SELECT id FROM restore_guard')" = 99
if "${repo_root}/deployments/sdw-backup" restore "${archive}" \
    "${restore_options[@]/${available_bytes}/1}" >/dev/null 2>&1; then
    printf 'insufficient PostgreSQL capacity unexpectedly restored\n' >&2
    exit 1
fi
test "$(psql --no-psqlrc -Atc 'SELECT id FROM restore_guard')" = 99

mkdir "${drill_root}/fault-bin"
real_pg_restore=$(command -v pg_restore)
cat >"${drill_root}/fault-bin/pg_restore" <<EOF
#!/usr/bin/env bash
case " \$* " in
    *" sdw_restore_"*) exit 73 ;;
esac
exec "${real_pg_restore}" "\$@"
EOF
chmod +x "${drill_root}/fault-bin/pg_restore"
if PATH="${drill_root}/fault-bin:${PATH}" "${repo_root}/deployments/sdw-backup" \
    restore "${archive}" "${restore_options[@]}" >/dev/null 2>&1; then
    printf 'injected candidate failure unexpectedly restored\n' >&2
    exit 1
fi
test "$(psql --no-psqlrc -Atc 'SELECT id FROM restore_guard')" = 99
test "$(psql --no-psqlrc -Atc "SELECT count(*) FROM pg_database WHERE datname LIKE 'sdw_restore_%'")" = 0

"${repo_root}/deployments/sdw-backup" restore "${archive}" "${restore_options[@]}"
test "$(psql --no-psqlrc -Atc 'SELECT value FROM drill_items WHERE id = 1')" = round-trip
test "$(psql --no-psqlrc -Atc "SELECT to_regclass('public.restore_guard') IS NULL")" = t
test "$(psql --no-psqlrc -Atc \
    "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m','S','f') AND pg_get_userbyid(c.relowner) <> 'sdw_app'")" = 0
cmp "${drill_root}/password.json" "${drill_root}/restored/password.json"
cmp "${drill_root}/keys/key.xml" "${drill_root}/restored/keys/key.xml"
cmp "${drill_root}/plugins/example/manifest.json" \
    "${drill_root}/restored/plugins/example/manifest.json"
export PGUSER=sdw_app PGPASSWORD=app-password
test "$(psql --no-psqlrc -Atc 'SELECT value FROM drill_items WHERE id = 1')" = round-trip
printf 'backup restore smoke test passed\n'
