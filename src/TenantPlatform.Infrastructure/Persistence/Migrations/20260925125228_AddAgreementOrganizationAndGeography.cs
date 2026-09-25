using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementOrganizationAndGeography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_agreements_AccountId_GeographicAreaId",
                table: "agreements",
                columns: new[] { "AccountId", "GeographicAreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreements_AccountId_OrganizationElementId",
                table: "agreements",
                columns: new[] { "AccountId", "OrganizationElementId" });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
        }
    }
}
