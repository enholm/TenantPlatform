using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV2DomainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_network_ssids_buildings_BuildingId",
                table: "network_ssids");

            migrationBuilder.DropIndex(
                name: "IX_network_ssids_AccountId_BuildingId_Name",
                table: "network_ssids");

            migrationBuilder.DropIndex(
                name: "IX_network_ssids_BuildingId",
                table: "network_ssids");

            migrationBuilder.RenameColumn(
                name: "BuildingId",
                table: "network_ssids",
                newName: "NetworkEnvironmentId");

            migrationBuilder.AddColumn<Guid>(
                name: "NetworkEnvironmentId",
                table: "ssid_request_details",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "network_environments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Vendor = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_network_environments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_network_environments_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_network_environments_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ssid_request_details_NetworkEnvironmentId",
                table: "ssid_request_details",
                column: "NetworkEnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_network_ssids_AccountId_Name",
                table: "network_ssids",
                columns: new[] { "AccountId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_network_environments_AccountId",
                table: "network_environments",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_network_environments_BuildingId_Name",
                table: "network_environments",
                columns: new[] { "BuildingId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ssid_request_details_network_environments_NetworkEnvironmen~",
                table: "ssid_request_details",
                column: "NetworkEnvironmentId",
                principalTable: "network_environments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ssid_request_details_network_environments_NetworkEnvironmen~",
                table: "ssid_request_details");

            migrationBuilder.DropTable(
                name: "network_environments");

            migrationBuilder.DropIndex(
                name: "IX_ssid_request_details_NetworkEnvironmentId",
                table: "ssid_request_details");

            migrationBuilder.DropIndex(
                name: "IX_network_ssids_AccountId_Name",
                table: "network_ssids");

            migrationBuilder.DropColumn(
                name: "NetworkEnvironmentId",
                table: "ssid_request_details");

            migrationBuilder.RenameColumn(
                name: "NetworkEnvironmentId",
                table: "network_ssids",
                newName: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_network_ssids_AccountId_BuildingId_Name",
                table: "network_ssids",
                columns: new[] { "AccountId", "BuildingId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_network_ssids_BuildingId",
                table: "network_ssids",
                column: "BuildingId");

            migrationBuilder.AddForeignKey(
                name: "FK_network_ssids_buildings_BuildingId",
                table: "network_ssids",
                column: "BuildingId",
                principalTable: "buildings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
