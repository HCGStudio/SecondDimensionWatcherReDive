#!/usr/bin/env bash

set -Eeuo pipefail

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <image-reference> <amd64|arm64>" >&2
  exit 2
fi

image_reference="$1"
expected_architecture="$2"
case "$expected_architecture" in
  amd64)
    expected_kernel_architecture=x86_64
    ;;
  arm64)
    expected_kernel_architecture=aarch64
    ;;
  *)
    echo "Unsupported expected architecture: $expected_architecture" >&2
    exit 2
    ;;
esac

kernel_architecture=$(uname -m)
echo "Kernel architecture: $kernel_architecture"
if [ "$kernel_architecture" != "$expected_kernel_architecture" ]; then
  echo "Expected a native $expected_architecture runner, found kernel $kernel_architecture" >&2
  exit 1
fi

server_architecture=$(docker version --format '{{.Server.Arch}}')
echo "Docker server architecture: $server_architecture"
if [ "$server_architecture" != "$expected_architecture" ]; then
  echo "Expected Docker server $expected_architecture, found $server_architecture" >&2
  exit 1
fi

actual_architecture=$(docker image inspect \
  --format '{{.Architecture}}' "$image_reference")
echo "Container image architecture: $actual_architecture"
if [ "$actual_architecture" != "$expected_architecture" ]; then
  echo "Expected $image_reference to be $expected_architecture, found $actual_architecture" >&2
  exit 1
fi

smoke_id="${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-1}-${expected_architecture}-$$"
network="sdw-smoke-${smoke_id}"
database="sdw-smoke-db-${smoke_id}"
application="sdw-smoke-app-${smoke_id}"

cleanup() {
  exit_status=$?
  trap - EXIT
  if [ "$exit_status" -ne 0 ]; then
    docker logs "$database" 2>&1 || true
    docker logs "$application" 2>&1 || true
  fi
  docker rm -f "$application" "$database" >/dev/null 2>&1 || true
  docker network rm "$network" >/dev/null 2>&1 || true
  exit "$exit_status"
}
trap cleanup EXIT

docker network create "$network"
docker run --detach \
  --name "$database" \
  --network "$network" \
  --network-alias postgres \
  --env POSTGRES_USER=sdw \
  --env POSTGRES_PASSWORD=sdw_ci_password \
  --env POSTGRES_DB=sdw \
  postgres:16-alpine

for attempt in {1..30}; do
  if docker exec "$database" pg_isready --username sdw --dbname sdw; then
    break
  fi
  if [ "$attempt" -eq 30 ]; then
    echo "PostgreSQL did not become ready" >&2
    exit 1
  fi
  sleep 2
done

docker run --detach \
  --name "$application" \
  --network "$network" \
  --publish 127.0.0.1:5097:8080 \
  --env ASPNETCORE_URLS=http://+:8080 \
  --env ConnectionStrings__sdw='Host=postgres;Username=sdw;Password=sdw_ci_password;Database=sdw' \
  --env JwtSecret='ci-only-secret-at-least-32-characters' \
  --env PasswordFile=/tmp/password.json \
  --env DataProtection__KeyRingPath=/tmp/data-protection-keys \
  --env FileStore__Local=/tmp/downloads \
  --env DisableCors=true \
  "$image_reference"

homepage=""
for attempt in {1..60}; do
  if homepage=$(curl --fail --silent --show-error http://127.0.0.1:5097/); then
    break
  fi
  if ! docker inspect --format '{{.State.Running}}' "$application" | grep --quiet true; then
    echo "Application container exited before becoming healthy" >&2
    exit 1
  fi
  if [ "$attempt" -eq 60 ]; then
    echo "Application did not become healthy" >&2
    exit 1
  fi
  sleep 2
done

migration_count=$(docker exec "$database" \
  psql --username sdw --dbname sdw --tuples-only --no-align \
  --command 'SELECT COUNT(*) FROM "__EFMigrationsHistory";')
if [[ ! "$migration_count" =~ ^[0-9]+$ ]] || [ "$migration_count" -le 0 ]; then
  echo "Expected at least one applied EF Core migration, found: $migration_count" >&2
  exit 1
fi
grep --fixed-strings --quiet '<div id=app></div>' <<< "$homepage"
