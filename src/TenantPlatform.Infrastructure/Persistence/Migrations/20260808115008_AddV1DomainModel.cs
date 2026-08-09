using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV1DomainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "buildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressLine1 = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buildings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_buildings_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_definitions_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreferredLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_units_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_units_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_units_units_ParentUnitId",
                        column: x => x.ParentUnitId,
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_role_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_role_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_role_assignments_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_role_assignments_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_role_assignments_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_role_assignments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "network_ssids",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VlanId = table.Column<int>(type: "integer", nullable: true),
                    SecurityType = table.Column<int>(type: "integer", nullable: false),
                    IsBroadcast = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_network_ssids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_network_ssids_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_network_ssids_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_network_ssids_organizations_TenantOrganizationId",
                        column: x => x.TenantOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_network_ssids_units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "occupancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_occupancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_occupancies_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_occupancies_organizations_TenantOrganizationId",
                        column: x => x.TenantOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_occupancies_units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_requests_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_requests_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_requests_organizations_RequesterOrganizationId",
                        column: x => x.RequesterOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_requests_service_definitions_ServiceDefinitionId",
                        column: x => x.ServiceDefinitionId,
                        principalTable: "service_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_requests_units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_requests_users_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ssid_request_details",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    ExistingNetworkSsidId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RequestedVlanId = table.Column<int>(type: "integer", nullable: true),
                    RequestedSecurityType = table.Column<int>(type: "integer", nullable: true),
                    RequestedIsBroadcast = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ssid_request_details", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ssid_request_details_network_ssids_ExistingNetworkSsidId",
                        column: x => x.ExistingNetworkSsidId,
                        principalTable: "network_ssids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ssid_request_details_service_requests_ServiceRequestId",
                        column: x => x.ServiceRequestId,
                        principalTable: "service_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_buildings_AccountId",
                table: "buildings",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_network_ssids_AccountId",
                table: "network_ssids",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_network_ssids_AccountId_BuildingId_Name",
                table: "network_ssids",
                columns: new[] { "AccountId", "BuildingId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_network_ssids_BuildingId",
                table: "network_ssids",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_network_ssids_TenantOrganizationId",
                table: "network_ssids",
                column: "TenantOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_network_ssids_UnitId",
                table: "network_ssids",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_occupancies_AccountId",
                table: "occupancies",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_occupancies_TenantOrganizationId",
                table: "occupancies",
                column: "TenantOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_occupancies_TenantOrganizationId_UnitId_ValidFrom",
                table: "occupancies",
                columns: new[] { "TenantOrganizationId", "UnitId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_occupancies_UnitId",
                table: "occupancies",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_service_definitions_AccountId_Code",
                table: "service_definitions",
                columns: new[] { "AccountId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_AccountId",
                table: "service_requests",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_AccountId_CreatedAt",
                table: "service_requests",
                columns: new[] { "AccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_BuildingId",
                table: "service_requests",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_RequesterOrganizationId",
                table: "service_requests",
                column: "RequesterOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_RequesterUserId",
                table: "service_requests",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_ServiceDefinitionId",
                table: "service_requests",
                column: "ServiceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_Status",
                table: "service_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_UnitId",
                table: "service_requests",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ssid_request_details_ExistingNetworkSsidId",
                table: "ssid_request_details",
                column: "ExistingNetworkSsidId");

            migrationBuilder.CreateIndex(
                name: "IX_ssid_request_details_ServiceRequestId",
                table: "ssid_request_details",
                column: "ServiceRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_units_AccountId",
                table: "units",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_units_BuildingId",
                table: "units",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_units_ParentUnitId",
                table: "units",
                column: "ParentUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_assignments_AccountId",
                table: "user_role_assignments",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_assignments_BuildingId",
                table: "user_role_assignments",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_assignments_OrganizationId",
                table: "user_role_assignments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_assignments_UserId",
                table: "user_role_assignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_assignments_UserId_AccountId_OrganizationId_Build~",
                table: "user_role_assignments",
                columns: new[] { "UserId", "AccountId", "OrganizationId", "BuildingId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "occupancies");

            migrationBuilder.DropTable(
                name: "ssid_request_details");

            migrationBuilder.DropTable(
                name: "user_role_assignments");

            migrationBuilder.DropTable(
                name: "network_ssids");

            migrationBuilder.DropTable(
                name: "service_requests");

            migrationBuilder.DropTable(
                name: "service_definitions");

            migrationBuilder.DropTable(
                name: "units");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "buildings");
        }
    }
}
