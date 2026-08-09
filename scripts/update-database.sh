#!/bin/bash
set -e

echo "=========================================="
echo "TenantPlatform - Update Database"
echo "=========================================="

echo ""
echo "Building solution..."
dotnet build

echo ""
echo "Pending migrations:"
dotnet ef migrations list \
    --project src/TenantPlatform.Infrastructure \
    --startup-project src/TenantPlatform.Web

echo ""
echo "Updating database..."

dotnet ef database update \
    --project src/TenantPlatform.Infrastructure \
    --startup-project src/TenantPlatform.Web

echo ""
echo "✓ Database successfully updated."