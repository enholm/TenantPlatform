#!/usr/bin/env bash
# Disposable, local-only PostgreSQL cluster. Never uses docker/.env or an existing database.
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
postgres_bin="${POSTGRES_TEST_BIN:-}"
if [[ -z "$postgres_bin" ]]; then
    if command -v initdb >/dev/null 2>&1; then
        postgres_bin="$(dirname "$(command -v initdb)")"
    elif [[ -x /Applications/Postgres.app/Contents/Versions/latest/bin/initdb ]]; then
        postgres_bin=/Applications/Postgres.app/Contents/Versions/latest/bin
    else
        echo 'PostgreSQL server tools missing. Install PostgreSQL and put initdb/pg_ctl/createdb on PATH, or set POSTGRES_TEST_BIN.' >&2
        exit 1
    fi
fi
for tool in initdb pg_ctl createdb; do
    [[ -x "$postgres_bin/$tool" ]] || { echo "Missing PostgreSQL tool: $tool" >&2; exit 1; }
done
command -v dotnet >/dev/null 2>&1 || { echo 'dotnet is required.' >&2; exit 1; }

projects=("$@")
if [[ ${#projects[@]} -eq 0 ]]; then
    projects=(TenantPlatform.AgreementList.SmokeTests TenantPlatform.Agreements.SmokeTests TenantPlatform.Dashboard.SmokeTests)
fi
for project in "${projects[@]}"; do
    case "$project" in
        TenantPlatform.AgreementList.SmokeTests|TenantPlatform.Agreements.SmokeTests|TenantPlatform.Dashboard.SmokeTests|\
        TenantPlatform.AccountRegisters.SmokeTests|TenantPlatform.MeetingRooms.SmokeTests|TenantPlatform.Leasing.SmokeTests|TenantPlatform.OAuth.SmokeTests|TenantPlatform.Mcp.SmokeTests|\
        TenantPlatform.AgreementAnalysis.SmokeTests|TenantPlatform.AgreementFollowup.SmokeTests|TenantPlatform.AgreementNotice.SmokeTests|\
        TenantPlatform.AgreementPeriods.SmokeTests|TenantPlatform.AgreementProcessing.SmokeTests) ;;
        *) echo "Unsupported project for this isolated test runner: $project" >&2; exit 1 ;;
    esac
    [[ "$project" != */* && -f "$repository_root/tests/$project/$project.csproj" ]] || { echo "Unknown test project: $project" >&2; exit 1; }
done

# A short path avoids Unix-socket path length limits on macOS. mktemp + umask keeps it private.
umask 077
cluster_root="$(mktemp -d /tmp/tenant-agreement-tests.XXXXXX)"
cluster_data="$cluster_root/data"
cluster_socket="$cluster_root/socket"
cleanup() {
    local result=$?
    trap - EXIT
    if [[ -f "$cluster_data/postmaster.pid" ]]; then
        if ! "$postgres_bin/pg_ctl" -D "$cluster_data" -m fast -w stop >/dev/null 2>&1; then
            echo "Could not stop disposable PostgreSQL. Files retained at $cluster_root" >&2
            exit 1
        fi
    fi
    rm -rf -- "$cluster_root"
    exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
mkdir "$cluster_socket"
# Trust applies only to the socket under this user's mode-700 temporary directory.
# Host authentication is rejected and the server has no TCP listener; no password is needed or stored.
"$postgres_bin/initdb" -D "$cluster_data" --username=agreement_tests --auth-local=trust --auth-host=reject --encoding=UTF8 --locale=C > "$cluster_root/initdb.log" 2>&1 || { cat "$cluster_root/initdb.log" >&2; exit 1; }
"$postgres_bin/pg_ctl" -D "$cluster_data" -l "$cluster_root/postgres.log" \
    -o "-k $cluster_socket -p 55486 -c listen_addresses=''" -w start >/dev/null || { cat "$cluster_root/postgres.log" >&2; exit 1; }
cd "$repository_root"
created_databases=()
for project in "${projects[@]}"; do
    case "$project" in
        TenantPlatform.AccountRegisters.SmokeTests) database=tenant_account_register_tests; connection_variable=ACCOUNT_REGISTER_TEST_CONNECTION ;;
        TenantPlatform.MeetingRooms.SmokeTests) database=tenant_meeting_tests; connection_variable=MEETING_ROOM_TEST_CONNECTION ;;
        TenantPlatform.Leasing.SmokeTests) database=tenant_leasing_tests; connection_variable=LEASING_TEST_CONNECTION ;;
        TenantPlatform.Mcp.SmokeTests) database=tenant_mcp_tests; connection_variable=MCP_TEST_CONNECTION ;;
        TenantPlatform.OAuth.SmokeTests) database=tenant_oauth_tests; connection_variable=OAUTH_TEST_CONNECTION ;;
        *) database=tenant_agreement_tests; connection_variable=AGREEMENT_TEST_CONNECTION ;;
    esac
    if [[ " ${created_databases[*]:-} " != *" $database "* ]]; then
        "$postgres_bin/createdb" -h "$cluster_socket" -p 55486 -U agreement_tests "$database"
        created_databases+=("$database")
    fi
    export "$connection_variable=Host=$cluster_socket;Port=55486;Database=$database;Username=agreement_tests;Pooling=false"
    echo "Running $project with disposable local PostgreSQL"
    dotnet run --project "tests/$project/$project.csproj"
done
# Stopping/removing the owned cluster also removes fixtures left by older runners without schema cleanup.
echo 'All selected tests passed. Stopping and removing disposable PostgreSQL.'
