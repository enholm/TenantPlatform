using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.OAuth.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpAccessPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessGrants",
                schema: "oauth",
                table: "mcp_tools",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Audience",
                schema: "oauth",
                table: "mcp_tools",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UseClass",
                schema: "oauth",
                table: "mcp_tools",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AccessGrants",
                schema: "oauth",
                table: "mcp_servers",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Audience",
                schema: "oauth",
                table: "mcp_servers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
            // Explicit 6A compatibility: current role audience and inherited tool access.
            // Only tools already approved in 6A keep their formerly automatic execution behaviour.
            migrationBuilder.Sql("UPDATE oauth.mcp_servers SET \"Audience\" = 1, \"AccessGrants\" = '[]'");
            migrationBuilder.Sql("UPDATE oauth.mcp_tools SET \"Audience\" = 1, \"AccessGrants\" = '[]', \"UseClass\" = CASE WHEN \"Approved\" THEN 1 ELSE 0 END");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessGrants",
                schema: "oauth",
                table: "mcp_tools");

            migrationBuilder.DropColumn(
                name: "Audience",
                schema: "oauth",
                table: "mcp_tools");

            migrationBuilder.DropColumn(
                name: "UseClass",
                schema: "oauth",
                table: "mcp_tools");

            migrationBuilder.DropColumn(
                name: "AccessGrants",
                schema: "oauth",
                table: "mcp_servers");

            migrationBuilder.DropColumn(
                name: "Audience",
                schema: "oauth",
                table: "mcp_servers");
        }
    }
}
