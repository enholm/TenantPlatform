using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueServiceDefinitionProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_service_definition_providers_AccountId_ServiceDefinitionId_~",
                table: "service_definition_providers",
                columns: new[] { "AccountId", "ServiceDefinitionId", "ServiceProviderOrganizationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_service_definition_providers_AccountId_ServiceDefinitionId_~",
                table: "service_definition_providers");
        }
    }
}
