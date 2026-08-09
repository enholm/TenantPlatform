#!/bin/bash

if [ $# -ne 1 ]; then
    echo "Usage: $0 <MigrationName>"
    exit 1
fi

MIGRATION_NAME="$1"

dotnet ef migrations add "$MIGRATION_NAME" \
    --project src/TenantPlatform.Infrastructure \
    --startup-project src/TenantPlatform.Web \
    --output-dir Persistence/Migrations