using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceModel_V5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "service_definitions");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "service_definitions");

            migrationBuilder.DropColumn(
                name: "HelpText",
                table: "service_definition_fields");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "service_definition_fields");

            migrationBuilder.CreateTable(
                name: "service_definition_field_translations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDefinitionFieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HelpText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_definition_field_translations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_definition_field_translations_service_definition_fi~",
                        column: x => x.ServiceDefinitionFieldId,
                        principalTable: "service_definition_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_definition_field_translations_ServiceDefinitionFiel~",
                table: "service_definition_field_translations",
                columns: new[] { "ServiceDefinitionFieldId", "LanguageCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_definition_field_translations");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "service_definitions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "service_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HelpText",
                table: "service_definition_fields",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "service_definition_fields",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
