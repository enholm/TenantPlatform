using Npgsql;
namespace TenantPlatform.Infrastructure.PersonalPages;
public static class PersonalPagesPrivileges
{
    public static async Task VerifyAsync(string connectionString, CancellationToken ct)
    {
        await using var c = new NpgsqlConnection(connectionString); await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT EXISTS (SELECT FROM pg_roles WHERE rolname=current_user AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls))
              OR has_schema_privilege(current_user,'public','CREATE')
              OR EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname <> 'information_schema' AND c.relkind IN ('r','p','v','m','f')
                AND (pg_has_role(current_user,c.relowner,'USAGE') OR
                  ((n.nspname <> 'public' OR c.relname <> 'personal_page_sets') AND has_table_privilege(current_user,c.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'))))
            """, c) { CommandTimeout = 5 };
        if ((bool)(await cmd.ExecuteScalarAsync(ct))!) throw new InvalidOperationException("Pages administration requires a restricted definition writer.");
    }
}
