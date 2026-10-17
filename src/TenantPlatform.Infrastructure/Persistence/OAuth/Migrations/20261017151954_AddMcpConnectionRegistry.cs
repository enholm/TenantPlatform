using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.OAuth.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpConnectionRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_mcp_client_credentials",
                schema: "oauth",
                table: "mcp_client_credentials");

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionId",
                schema: "oauth",
                table: "mcp_client_credentials",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ProfileVersion",
                schema: "oauth",
                table: "mcp_client_credentials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_mcp_client_credentials",
                schema: "oauth",
                table: "mcp_client_credentials",
                columns: new[] { "UserId", "ConnectionId" });

            migrationBuilder.CreateTable(
                name: "mcp_servers",
                schema: "oauth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProfileVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialEpoch = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscoveredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DiscoveryComplete = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcp_servers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mcp_tools",
                schema: "oauth",
                columns: table => new
                {
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    InputSchema = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    OutputSchema = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: true),
                    DiscoveredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcp_tools", x => new { x.ConnectionId, x.Name });
                    table.ForeignKey(
                        name: "FK_mcp_tools_mcp_servers_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "oauth",
                        principalTable: "mcp_servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mcp_servers_AccountId",
                schema: "oauth",
                table: "mcp_servers",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_tools",
                schema: "oauth");

            migrationBuilder.DropTable(
                name: "mcp_servers",
                schema: "oauth");

            migrationBuilder.DropPrimaryKey(
                name: "PK_mcp_client_credentials",
                schema: "oauth",
                table: "mcp_client_credentials");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                schema: "oauth",
                table: "mcp_client_credentials");

            migrationBuilder.DropColumn(
                name: "ProfileVersion",
                schema: "oauth",
                table: "mcp_client_credentials");

            migrationBuilder.AddPrimaryKey(
                name: "PK_mcp_client_credentials",
                schema: "oauth",
                table: "mcp_client_credentials",
                column: "UserId");
        }
    }
}
