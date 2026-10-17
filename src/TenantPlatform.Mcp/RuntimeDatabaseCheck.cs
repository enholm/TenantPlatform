using Npgsql;

namespace TenantPlatform.Mcp;

public static class RuntimeDatabaseCheck
{
    // Technical startup validation only: SELECT catalogs, never create/repair roles or schema.
    public static async Task VerifyAsync(McpOptions options, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (SELECT FROM pg_roles WHERE rolname=current_user
                AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls))
                OR has_schema_privilege(current_user, 'public', 'CREATE')
                OR EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname <> 'information_schema'
                    AND c.relkind IN ('r','p','v','m','f')
                    AND (has_table_privilege(current_user,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_has_role(current_user,c.relowner,'USAGE')))
            """, connection) { CommandTimeout = 8 };
        if ((bool)(await command.ExecuteScalarAsync(ct))!)
            throw new InvalidOperationException("MCP requires a dedicated database reader without ownership or write privileges.");
    }
}
