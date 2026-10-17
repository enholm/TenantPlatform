#!/bin/bash

if [ $# -lt 1 ] || [ $# -gt 2 ]; then
    echo "Usage: $0 <MigrationName> [TenantPlatformDbContext|OAuthDbContext]"
    exit 1
fi

MIGRATION_NAME="$1"
MIGRATION_CONTEXT="${2:-TenantPlatformDbContext}"
case "$MIGRATION_CONTEXT" in
    TenantPlatformDbContext) MIGRATION_DIRECTORY=Persistence/Migrations ;;
    OAuthDbContext) MIGRATION_DIRECTORY=Persistence/OAuth/Migrations ;;
    *) echo "Unsupported migration context" >&2; exit 1 ;;
esac

dotnet ef migrations add "$MIGRATION_NAME" \
    --project src/TenantPlatform.Infrastructure \
    --startup-project src/TenantPlatform.Web \
    --context "$MIGRATION_CONTEXT" \
    --output-dir "$MIGRATION_DIRECTORY"
