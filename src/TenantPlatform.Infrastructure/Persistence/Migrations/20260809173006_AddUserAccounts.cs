using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_role_assignments");

            migrationBuilder.CreateTable(
                name: "LoginAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastLoginUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoginAccounts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_accounts_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_accounts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_account_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_account_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_account_roles_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_account_roles_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_account_roles_user_accounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAccounts_UserId",
                table: "LoginAccounts",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_account_roles_BuildingId",
                table: "user_account_roles",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_user_account_roles_OrganizationId",
                table: "user_account_roles",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_user_account_roles_UserAccountId",
                table: "user_account_roles",
                column: "UserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_user_account_roles_UserAccountId_OrganizationId_BuildingId_~",
                table: "user_account_roles",
                columns: new[] { "UserAccountId", "OrganizationId", "BuildingId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_AccountId",
                table: "user_accounts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_UserId",
                table: "user_accounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_UserId_AccountId",
                table: "user_accounts",
                columns: new[] { "UserId", "AccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoginAccounts");

            migrationBuilder.DropTable(
                name: "user_account_roles");

            migrationBuilder.DropTable(
                name: "user_accounts");

            migrationBuilder.CreateTable(
                name: "user_role_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
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
        }
    }
}
