using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceModel_V4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "service_requests",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReplyToken",
                table: "service_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "service_requests",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "service_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "HandlerType",
                table: "service_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "service_definitions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "service_definitions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "service_definition_fields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FieldType = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    HelpText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_definition_fields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_definition_fields_service_definitions_ServiceDefini~",
                        column: x => x.ServiceDefinitionId,
                        principalTable: "service_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_definition_providers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceProviderOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationType = table.Column<int>(type: "integer", nullable: false),
                    RequestEmailAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_definition_providers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_definition_providers_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_definition_providers_organizations_ServiceProviderO~",
                        column: x => x.ServiceProviderOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_definition_providers_service_definitions_ServiceDef~",
                        column: x => x.ServiceDefinitionId,
                        principalTable: "service_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_request_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ToAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    ExternalMessageId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExternalThreadId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_request_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_request_messages_service_requests_ServiceRequestId",
                        column: x => x.ServiceRequestId,
                        principalTable: "service_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_service_request_messages_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_request_field_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDefinitionFieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_request_field_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_request_field_values_service_definition_fields_Serv~",
                        column: x => x.ServiceDefinitionFieldId,
                        principalTable: "service_definition_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_request_field_values_service_requests_ServiceReques~",
                        column: x => x.ServiceRequestId,
                        principalTable: "service_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_AssignedServiceProviderOrganizationId",
                table: "service_requests",
                column: "AssignedServiceProviderOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_ReplyToken",
                table: "service_requests",
                column: "ReplyToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_definitions_AccountId_Category",
                table: "service_definitions",
                columns: new[] { "AccountId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_service_definition_fields_ServiceDefinitionId_Key",
                table: "service_definition_fields",
                columns: new[] { "ServiceDefinitionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_definition_fields_ServiceDefinitionId_SortOrder",
                table: "service_definition_fields",
                columns: new[] { "ServiceDefinitionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_service_definition_providers_AccountId",
                table: "service_definition_providers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_service_definition_providers_ServiceDefinitionId_ServicePro~",
                table: "service_definition_providers",
                columns: new[] { "ServiceDefinitionId", "ServiceProviderOrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_definition_providers_ServiceProviderOrganizationId",
                table: "service_definition_providers",
                column: "ServiceProviderOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_field_values_ServiceDefinitionFieldId",
                table: "service_request_field_values",
                column: "ServiceDefinitionFieldId");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_field_values_ServiceRequestId_ServiceDefini~",
                table: "service_request_field_values",
                columns: new[] { "ServiceRequestId", "ServiceDefinitionFieldId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_request_messages_CreatedByUserId",
                table: "service_request_messages",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_messages_ExternalMessageId",
                table: "service_request_messages",
                column: "ExternalMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_messages_ServiceRequestId",
                table: "service_request_messages",
                column: "ServiceRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_messages_ServiceRequestId_CreatedAt",
                table: "service_request_messages",
                columns: new[] { "ServiceRequestId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_service_requests_organizations_AssignedServiceProviderOrgan~",
                table: "service_requests",
                column: "AssignedServiceProviderOrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_service_requests_organizations_AssignedServiceProviderOrgan~",
                table: "service_requests");

            migrationBuilder.DropTable(
                name: "service_definition_providers");

            migrationBuilder.DropTable(
                name: "service_request_field_values");

            migrationBuilder.DropTable(
                name: "service_request_messages");

            migrationBuilder.DropTable(
                name: "service_definition_fields");

            migrationBuilder.DropIndex(
                name: "IX_service_requests_AssignedServiceProviderOrganizationId",
                table: "service_requests");

            migrationBuilder.DropIndex(
                name: "IX_service_requests_ReplyToken",
                table: "service_requests");

            migrationBuilder.DropIndex(
                name: "IX_service_definitions_AccountId_Category",
                table: "service_definitions");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "service_requests",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReplyToken",
                table: "service_requests",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "service_requests",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "service_definitions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "HandlerType",
                table: "service_definitions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "service_definitions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "service_definitions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
