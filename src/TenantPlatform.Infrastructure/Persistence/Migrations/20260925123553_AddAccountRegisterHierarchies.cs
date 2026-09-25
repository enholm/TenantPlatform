using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    public partial class AddAccountRegisterHierarchies : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename instead of recreating tables: preserve IDs and all existing register data.
            migrationBuilder.DropForeignKey("FK_departments_accounts_AccountId", "departments");
            migrationBuilder.DropPrimaryKey("PK_departments", "departments");
            migrationBuilder.RenameTable(name: "departments", newName: "organization_elements");
            migrationBuilder.RenameIndex(name: "IX_departments_AccountId_Name", table: "organization_elements", newName: "IX_organization_elements_AccountId_Name");
            migrationBuilder.AddPrimaryKey("PK_organization_elements", "organization_elements", "Id");
            migrationBuilder.AddColumn<Guid>(name: "ParentId", table: "organization_elements", type: "uuid", nullable: true);
            migrationBuilder.AddUniqueConstraint("AK_organization_elements_AccountId_Id", "organization_elements", new[] { "AccountId", "Id" });
            migrationBuilder.CreateIndex("IX_organization_elements_AccountId_ParentId", "organization_elements", new[] { "AccountId", "ParentId" });
            migrationBuilder.AddForeignKey(name: "FK_organization_elements_accounts_AccountId", table: "organization_elements", column: "AccountId",
                principalTable: "accounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_organization_elements_organization_elements_AccountId_Paren~", table: "organization_elements", columns: new[] { "AccountId", "ParentId" },
                principalTable: "organization_elements", principalColumns: new[] { "AccountId", "Id" }, onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey("FK_locations_accounts_AccountId", "locations");
            migrationBuilder.DropPrimaryKey("PK_locations", "locations");
            migrationBuilder.RenameTable(name: "locations", newName: "geographic_areas");
            migrationBuilder.RenameIndex(name: "IX_locations_AccountId_Name", table: "geographic_areas", newName: "IX_geographic_areas_AccountId_Name");
            migrationBuilder.AddPrimaryKey("PK_geographic_areas", "geographic_areas", "Id");
            migrationBuilder.AddColumn<Guid>(name: "ParentId", table: "geographic_areas", type: "uuid", nullable: true);
            migrationBuilder.AddUniqueConstraint("AK_geographic_areas_AccountId_Id", "geographic_areas", new[] { "AccountId", "Id" });
            migrationBuilder.CreateIndex("IX_geographic_areas_AccountId_ParentId", "geographic_areas", new[] { "AccountId", "ParentId" });
            migrationBuilder.AddForeignKey(name: "FK_geographic_areas_accounts_AccountId", table: "geographic_areas", column: "AccountId",
                principalTable: "accounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_geographic_areas_geographic_areas_AccountId_ParentId", table: "geographic_areas", columns: new[] { "AccountId", "ParentId" },
                principalTable: "geographic_areas", principalColumns: new[] { "AccountId", "Id" }, onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey("FK_organization_elements_organization_elements_AccountId_Paren~", "organization_elements");
            migrationBuilder.DropForeignKey("FK_organization_elements_accounts_AccountId", "organization_elements");
            migrationBuilder.DropIndex("IX_organization_elements_AccountId_ParentId", "organization_elements");
            migrationBuilder.DropUniqueConstraint("AK_organization_elements_AccountId_Id", "organization_elements");
            migrationBuilder.DropColumn("ParentId", "organization_elements");
            migrationBuilder.DropPrimaryKey("PK_organization_elements", "organization_elements");
            migrationBuilder.RenameTable(name: "organization_elements", newName: "departments");
            migrationBuilder.RenameIndex(name: "IX_organization_elements_AccountId_Name", table: "departments", newName: "IX_departments_AccountId_Name");
            migrationBuilder.AddPrimaryKey("PK_departments", "departments", "Id");
            migrationBuilder.AddForeignKey(name: "FK_departments_accounts_AccountId", table: "departments", column: "AccountId",
                principalTable: "accounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey("FK_geographic_areas_geographic_areas_AccountId_ParentId", "geographic_areas");
            migrationBuilder.DropForeignKey("FK_geographic_areas_accounts_AccountId", "geographic_areas");
            migrationBuilder.DropIndex("IX_geographic_areas_AccountId_ParentId", "geographic_areas");
            migrationBuilder.DropUniqueConstraint("AK_geographic_areas_AccountId_Id", "geographic_areas");
            migrationBuilder.DropColumn("ParentId", "geographic_areas");
            migrationBuilder.DropPrimaryKey("PK_geographic_areas", "geographic_areas");
            migrationBuilder.RenameTable(name: "geographic_areas", newName: "locations");
            migrationBuilder.RenameIndex(name: "IX_geographic_areas_AccountId_Name", table: "locations", newName: "IX_locations_AccountId_Name");
            migrationBuilder.AddPrimaryKey("PK_locations", "locations", "Id");
            migrationBuilder.AddForeignKey(name: "FK_locations_accounts_AccountId", table: "locations", column: "AccountId",
                principalTable: "accounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }
    }
}
