using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyAgreementClassifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agreements_geographic_areas_AccountId_GeographicAreaId",
                table: "agreements");

            migrationBuilder.DropForeignKey(
                name: "FK_agreements_organization_elements_AccountId_OrganizationElem~",
                table: "agreements");

            migrationBuilder.DropIndex(
                name: "IX_agreements_AccountId_GeographicAreaId",
                table: "agreements");

            migrationBuilder.DropIndex(
                name: "IX_agreements_AccountId_OrganizationElementId",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "GeographicAreaId",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "OrganizationElementId",
                table: "agreements");

            migrationBuilder.DropTable(
                name: "geographic_areas");

            migrationBuilder.DropTable(
                name: "organization_elements");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Schema reversal only; dropped classification data cannot be restored.
            migrationBuilder.AddColumn<Guid>(
                name: "GeographicAreaId",
                table: "agreements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationElementId",
                table: "agreements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "geographic_areas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_geographic_areas", x => x.Id);
                    table.UniqueConstraint("AK_geographic_areas_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_geographic_areas_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_geographic_areas_geographic_areas_AccountId_ParentId",
                        columns: x => new { x.AccountId, x.ParentId },
                        principalTable: "geographic_areas",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organization_elements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_elements", x => x.Id);
                    table.UniqueConstraint("AK_organization_elements_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_organization_elements_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_organization_elements_organization_elements_AccountId_Paren~",
                        columns: x => new { x.AccountId, x.ParentId },
                        principalTable: "organization_elements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreements_AccountId_GeographicAreaId",
                table: "agreements",
                columns: new[] { "AccountId", "GeographicAreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreements_AccountId_OrganizationElementId",
                table: "agreements",
                columns: new[] { "AccountId", "OrganizationElementId" });

            migrationBuilder.CreateIndex(
                name: "IX_geographic_areas_AccountId_Name",
                table: "geographic_areas",
                columns: new[] { "AccountId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_geographic_areas_AccountId_ParentId",
                table: "geographic_areas",
                columns: new[] { "AccountId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_elements_AccountId_Name",
                table: "organization_elements",
                columns: new[] { "AccountId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_elements_AccountId_ParentId",
                table: "organization_elements",
                columns: new[] { "AccountId", "ParentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_agreements_geographic_areas_AccountId_GeographicAreaId",
                table: "agreements",
                columns: new[] { "AccountId", "GeographicAreaId" },
                principalTable: "geographic_areas",
                principalColumns: new[] { "AccountId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_agreements_organization_elements_AccountId_OrganizationElem~",
                table: "agreements",
                columns: new[] { "AccountId", "OrganizationElementId" },
                principalTable: "organization_elements",
                principalColumns: new[] { "AccountId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
