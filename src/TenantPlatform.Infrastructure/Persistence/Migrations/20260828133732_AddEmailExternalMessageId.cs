using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailExternalMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_service_request_messages_ExternalMessageId",
                table: "service_request_messages");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_messages_ExternalMessageId",
                table: "service_request_messages",
                column: "ExternalMessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_service_request_messages_ExternalMessageId",
                table: "service_request_messages");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_messages_ExternalMessageId",
                table: "service_request_messages",
                column: "ExternalMessageId");
        }
    }
}
