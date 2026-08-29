using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceRequestMessageExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InReplyToMessageId",
                table: "service_request_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "References",
                table: "service_request_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InReplyToMessageId",
                table: "email_outbox_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "References",
                table: "email_outbox_messages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InReplyToMessageId",
                table: "service_request_messages");

            migrationBuilder.DropColumn(
                name: "References",
                table: "service_request_messages");

            migrationBuilder.DropColumn(
                name: "InReplyToMessageId",
                table: "email_outbox_messages");

            migrationBuilder.DropColumn(
                name: "References",
                table: "email_outbox_messages");
        }
    }
}
