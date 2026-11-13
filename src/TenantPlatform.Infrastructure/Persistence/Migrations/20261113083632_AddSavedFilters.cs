using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "saved_filters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CriteriaVersion = table.Column<int>(type: "integer", nullable: false),
                    CriteriaJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_filters", x => x.Id);
                    table.CheckConstraint("CK_saved_filters_criteria_version", "\"CriteriaVersion\" > 0");
                    table.CheckConstraint("CK_saved_filters_page", "\"Page\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_saved_filters_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_saved_filters_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_saved_filters_AccountId_UserId_Page_Name",
                table: "saved_filters",
                columns: new[] { "AccountId", "UserId", "Page", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_saved_filters_AccountId_UserId_Page_NormalizedName",
                table: "saved_filters",
                columns: new[] { "AccountId", "UserId", "Page", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_saved_filters_UserId",
                table: "saved_filters",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saved_filters");
        }
    }
}
