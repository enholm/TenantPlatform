using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImproveServiceDefinitionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OptionsJson",
                table: "service_definition_fields",
                newName: "options");

            migrationBuilder.AddColumn<string>(
                name: "Placeholder",
                table: "service_definition_field_translations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Placeholder",
                table: "service_definition_field_translations");

            migrationBuilder.RenameColumn(
                name: "options",
                table: "service_definition_fields",
                newName: "OptionsJson");
        }
    }
}
