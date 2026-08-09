using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceDefinitionTranslations : Migration
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

            migrationBuilder.CreateTable(
                name: "service_definition_translations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_definition_translations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_definition_translations_service_definitions_Service~",
                        column: x => x.ServiceDefinitionId,
                        principalTable: "service_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_definition_translations_ServiceDefinitionId",
                table: "service_definition_translations",
                column: "ServiceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_service_definition_translations_ServiceDefinitionId_Languag~",
                table: "service_definition_translations",
                columns: new[] { "ServiceDefinitionId", "LanguageCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_definition_translations");

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
        }
    }
}
