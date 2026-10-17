using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.OAuth.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalMcpCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mcp_client_credentials",
                schema: "oauth",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Issuer = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Resource = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ProtectedTokens = table.Column<string>(type: "text", nullable: true),
                    GrantId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GrantExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PendingId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcp_client_credentials", x => x.UserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_client_credentials",
                schema: "oauth");
        }
    }
}
