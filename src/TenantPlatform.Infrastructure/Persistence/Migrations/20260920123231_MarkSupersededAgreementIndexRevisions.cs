using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarkSupersededAgreementIndexRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE agreement_index_values old
                SET "Superseded" = true
                WHERE NOT old."Superseded" AND EXISTS (
                    SELECT 1 FROM agreement_index_values newer
                    WHERE newer."AccountId" = old."AccountId"
                      AND newer."IndexId" = old."IndexId"
                      AND newer."PeriodKey" = old."PeriodKey"
                      AND newer."Revision" > old."Revision"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Preserve corrected history flags; their previous values cannot be reconstructed.
        }
    }
}
