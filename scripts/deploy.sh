#!/usr/bin/env bash

set -Eeuo pipefail

REMOTE_USER="morten"
REMOTE_HOST="10.150.100.242"
REMOTE_PATH="/opt/tenantplatform"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo "Deploying TenantPlatform to ${REMOTE_USER}@${REMOTE_HOST}..."

rsync \
  --archive \
  --verbose \
  --compress \
  --delete \
  --exclude '.git/' \
  --exclude '.vscode/' \
  --exclude '**/bin/' \
  --exclude '**/obj/' \
  --exclude 'docker/.env' \
  "${PROJECT_ROOT}/" \
  "${REMOTE_USER}@${REMOTE_HOST}:${REMOTE_PATH}/"

ssh "${REMOTE_USER}@${REMOTE_HOST}" <<EOF
set -Eeuo pipefail

cd "${REMOTE_PATH}"

docker compose \
  --env-file docker/.env \
  -f docker/compose.yml \
  up \
  --detach \
  --build \
  --remove-orphans

docker compose \
  --env-file docker/.env \
  -f docker/compose.yml \
  ps
EOF

echo "Deployment completed."