#!/usr/bin/env bash
set -euo pipefail

image="${1:-}"
deb_package="${2:-}"
if [[ -z "$image" || -z "$deb_package" || ! -f "$deb_package" ]]; then
    echo "usage: $0 IMAGE /path/to/sdw-redive.deb" >&2
    exit 2
fi
if ! command -v apt-get >/dev/null 2>&1; then
    echo "delivery smoke requires apt-get to resolve Debian package dependencies" >&2
    exit 2
fi
deb_package="$(realpath "$deb_package")"
declared_dependencies="$(dpkg-deb --field "$deb_package" Depends)"
if ! grep -Eq '(^|,)[[:space:]]*aspnetcore-runtime-10\.0([[:space:]]|\(|,|$)' \
    <<<"$declared_dependencies"; then
    echo "Debian package does not declare aspnetcore-runtime-10.0: $declared_dependencies" >&2
    exit 2
fi
if dpkg-query -W -f='${db:Status-Status}' sdw-redive 2>/dev/null | grep -q '^installed$'; then
    echo "refusing to overwrite an existing sdw-redive installation" >&2
    exit 2
fi
if getent passwd sdw-redive >/dev/null 2>&1 || getent group sdw-redive >/dev/null 2>&1; then
    echo "refusing to reuse an existing sdw-redive system account" >&2
    exit 2
fi
if [[ -e /var/lib/sdw-redive || -e /etc/sdw-redive ]]; then
    echo "refusing to overwrite existing sdw-redive data or configuration" >&2
    exit 2
fi

smoke_root="$(mktemp -d)"
suffix="${GITHUB_RUN_ID:-$$}-${RANDOM}"
database_container="sdw-delivery-db-$suffix"
application_container="sdw-delivery-app-$suffix"
network="sdw-delivery-$suffix"
package_pid=""
package_installed=0
account_created=0

cleanup() {
    set +e
    if [[ -n "$package_pid" ]]; then
        kill "$package_pid" 2>/dev/null
        wait "$package_pid" 2>/dev/null
    fi
    docker rm --force "$application_container" >/dev/null 2>&1
    docker rm --force "$database_container" >/dev/null 2>&1
    docker network rm "$network" >/dev/null 2>&1
    if [[ "$package_installed" == "1" ]]; then
        sudo env DEBIAN_FRONTEND=noninteractive \
            apt-get purge --yes sdw-redive >/dev/null 2>&1 ||
            sudo dpkg --purge sdw-redive >/dev/null 2>&1
    fi
    if [[ "$account_created" == "1" ]]; then
        sudo userdel sdw-redive >/dev/null 2>&1
        sudo groupdel sdw-redive >/dev/null 2>&1
    fi
    sudo rm -rf /var/lib/sdw-redive /etc/sdw-redive
    rm -rf "$smoke_root"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

wait_for_url() {
    local url="$1"
    local process_kind="$2"
    for _ in {1..120}; do
        if curl --silent --fail "$url" >/dev/null; then
            return 0
        fi
        if [[ "$process_kind" == "container" ]] &&
            ! docker inspect --format '{{.State.Running}}' "$application_container" 2>/dev/null | grep -q true; then
            docker logs "$application_container" >&2 || true
            return 1
        fi
        if [[ "$process_kind" == "package" ]] && ! kill -0 "$package_pid" 2>/dev/null; then
            cat "$smoke_root/package.log" >&2
            return 1
        fi
        sleep 0.5
    done
    echo "timed out waiting for $url" >&2
    return 1
}

docker network create "$network" >/dev/null
docker run --detach --name "$database_container" --network "$network" \
    --network-alias postgres \
    --env POSTGRES_USER=postgres \
    --env POSTGRES_PASSWORD=postgres \
    --env POSTGRES_DB=sdw_container \
    --publish 127.0.0.1::5432 \
    postgres:17-alpine >/dev/null
database_ready=0
for _ in {1..120}; do
    # The official image briefly starts a temporary postmaster while initializing
    # PGDATA. Waiting for PID 1 to become postgres avoids treating that transient
    # server as ready immediately before it shuts down.
    if docker exec "$database_container" sh -c \
        'test "$(cat /proc/1/comm)" = postgres' >/dev/null 2>&1 &&
        docker exec "$database_container" pg_isready \
            --username postgres --dbname sdw_container >/dev/null 2>&1; then
        database_ready=1
        break
    fi
    if ! docker inspect --format '{{.State.Running}}' "$database_container" 2>/dev/null | grep -q true; then
        docker logs "$database_container" >&2 || true
        exit 1
    fi
    sleep 0.25
done
if [[ "$database_ready" != "1" ]]; then
    docker logs "$database_container" >&2 || true
    echo "timed out waiting for the final PostgreSQL server" >&2
    exit 1
fi
docker exec "$database_container" createdb --username postgres sdw_package
database_port="$(docker port "$database_container" 5432/tcp | sed -n 's/.*://p')"

docker run --detach --name "$application_container" --network "$network" \
    --publish 127.0.0.1::8080 \
    --env ASPNETCORE_URLS=http://0.0.0.0:8080 \
    --env ASPNETCORE_ENVIRONMENT=Production \
    --env 'ConnectionStrings__sdw=Host=postgres;Username=postgres;Password=postgres;Database=sdw_container' \
    --env 'JwtSecret=delivery-smoke-jwt-secret-with-at-least-32-characters' \
    --env 'PasswordFile=/tmp/sdw-password.json' \
    --env 'DataProtection__KeyRingPath=/tmp/sdw-keys' \
    --env 'FileStore__Local=/tmp/sdw-downloads' \
    "$image" >/dev/null
application_port="$(docker port "$application_container" 8080/tcp | sed -n 's/.*://p')"
wait_for_url "http://127.0.0.1:$application_port/" container
curl --silent --fail "http://127.0.0.1:$application_port/" | grep -Eqi '<!doctype html>'
docker exec "$database_container" psql --username postgres --dbname sdw_container \
    --tuples-only --no-align --command \
    'SELECT COUNT(*) FROM "__EFMigrationsHistory";' | grep -Eq '^[1-9][0-9]*$'
docker exec "$database_container" psql --username postgres --dbname sdw_container \
    --tuples-only --no-align --command \
    "SELECT to_regclass('\"ApplicationSettings\"') IS NOT NULL;" | grep -qx t
docker rm --force "$application_container" >/dev/null

sudo apt-get update
package_installed=1
account_created=1
# The unprivileged runner shell owns smoke_root and intentionally captures apt's output.
# shellcheck disable=SC2024
if ! sudo env DEBIAN_FRONTEND=noninteractive \
    apt-get install --yes --no-install-recommends "$deb_package" \
    >"$smoke_root/apt-install.log" 2>&1; then
    cat "$smoke_root/apt-install.log" >&2
    exit 1
fi
dpkg-query -W -f='${db:Status-Status}\n' sdw-redive | grep -qx installed
dpkg-query -W -f='${db:Status-Status}\n' aspnetcore-runtime-10.0 | grep -qx installed
sudo apt-get check
/usr/bin/dotnet --list-runtimes | grep -Eq '^Microsoft\.AspNetCore\.App 10\.'

# The unprivileged runner shell intentionally captures the service process output.
# shellcheck disable=SC2024
sudo --user=sdw-redive env \
    ASPNETCORE_URLS=http://127.0.0.1:15099 \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_CONTENTROOT=/usr/lib/sdw-redive \
    "ConnectionStrings__sdw=Host=127.0.0.1;Port=$database_port;Username=postgres;Password=postgres;Database=sdw_package" \
    'JwtSecret=delivery-smoke-jwt-secret-with-at-least-32-characters' \
    'PasswordFile=/var/lib/sdw-redive/smoke-password.json' \
    'DataProtection__KeyRingPath=/var/lib/sdw-redive/data-protection-keys' \
    'FileStore__Local=/var/lib/sdw-redive/downloads' \
    /usr/bin/dotnet /usr/lib/sdw-redive/SecondDimensionWatcherReDive.dll \
    >"$smoke_root/package.log" 2>&1 &
package_pid="$!"
wait_for_url http://127.0.0.1:15099/ package
curl --silent --fail http://127.0.0.1:15099/ | grep -Eqi '<!doctype html>'
docker exec "$database_container" psql --username postgres --dbname sdw_package \
    --tuples-only --no-align --command \
    'SELECT COUNT(*) FROM "__EFMigrationsHistory";' | grep -Eq '^[1-9][0-9]*$'

kill "$package_pid"
wait "$package_pid" 2>/dev/null || true
package_pid=""
sudo env DEBIAN_FRONTEND=noninteractive apt-get purge --yes sdw-redive >/dev/null
package_installed=0
test "$(dpkg-query -W -f='${db:Status-Status}' sdw-redive 2>/dev/null || true)" != "installed"
sudo apt-get check

echo "Container and Debian package delivery smoke passed"
