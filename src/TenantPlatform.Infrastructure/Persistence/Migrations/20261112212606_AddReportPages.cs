using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_pages", x => x.Id);
                    table.UniqueConstraint("AK_report_pages_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_report_pages_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_report_pages_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_report_pages_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_report_pages_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "report_cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportPageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Module = table.Column<int>(type: "integer", nullable: false),
                    DefinitionVersion = table.Column<int>(type: "integer", nullable: false),
                    FiltersJson = table.Column<string>(type: "jsonb", nullable: false),
                    PeriodRule = table.Column<int>(type: "integer", nullable: false),
                    CustomFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomTo = table.Column<DateOnly>(type: "date", nullable: true),
                    DateBasisId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MetricId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AggregationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GroupingKind = table.Column<int>(type: "integer", nullable: false),
                    GroupingKey = table.Column<int>(type: "integer", nullable: true),
                    GroupingScope = table.Column<int>(type: "integer", nullable: true),
                    GroupingDimensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TimeGrouping = table.Column<int>(type: "integer", nullable: true),
                    Visualization = table.Column<int>(type: "integer", nullable: true),
                    DisplayJson = table.Column<string>(type: "jsonb", nullable: false),
                    GridColumn = table.Column<int>(type: "integer", nullable: false),
                    GridRow = table.Column<int>(type: "integer", nullable: false),
                    GridWidth = table.Column<int>(type: "integer", nullable: false),
                    GridHeight = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_cards", x => x.Id);
                    table.UniqueConstraint("AK_report_cards_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.CheckConstraint("CK_report_cards_custom_period", "(\"PeriodRule\" = 17 AND \"CustomFrom\" IS NOT NULL AND \"CustomTo\" IS NOT NULL AND \"CustomFrom\" <= \"CustomTo\") OR (\"PeriodRule\" <> 17 AND \"CustomFrom\" IS NULL AND \"CustomTo\" IS NULL)");
                    table.CheckConstraint("CK_report_cards_definition_version", "\"DefinitionVersion\" > 0");
                    table.CheckConstraint("CK_report_cards_grouping_kind", "\"GroupingKind\" IN (0,1,2,3)");
                    table.CheckConstraint("CK_report_cards_grouping_shape", "(\"GroupingKind\" = 0 AND \"GroupingKey\" IS NULL AND \"GroupingScope\" IS NULL AND \"GroupingDimensionId\" IS NULL AND \"TimeGrouping\" IS NULL) OR (\"GroupingKind\" = 1 AND \"GroupingKey\" IS NULL AND \"GroupingScope\" IS NULL AND \"GroupingDimensionId\" IS NULL AND \"TimeGrouping\" IS NOT NULL) OR (\"GroupingKind\" = 2 AND \"GroupingKey\" IS NOT NULL AND \"GroupingScope\" IS NULL AND \"GroupingDimensionId\" IS NULL AND \"TimeGrouping\" IS NULL) OR (\"GroupingKind\" = 3 AND \"GroupingKey\" IS NULL AND \"GroupingScope\" IS NOT NULL AND \"GroupingDimensionId\" IS NOT NULL AND \"TimeGrouping\" IS NULL)");
                    table.CheckConstraint("CK_report_cards_layout", "\"GridColumn\" BETWEEN 1 AND 12 AND \"GridRow\" > 0 AND \"GridWidth\" BETWEEN 1 AND 12 AND \"GridHeight\" > 0 AND \"SortOrder\" >= 0");
                    table.CheckConstraint("CK_report_cards_module", "\"Module\" IN (0,1)");
                    table.CheckConstraint("CK_report_cards_period_rule", "\"PeriodRule\" BETWEEN 0 AND 17");
                    table.CheckConstraint("CK_report_cards_time_grouping", "\"TimeGrouping\" IS NULL OR \"TimeGrouping\" BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_report_cards_visualization", "\"Visualization\" IS NULL OR \"Visualization\" BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_report_cards_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_report_cards_report_pages_AccountId_ReportPageId",
                        columns: x => new { x.AccountId, x.ReportPageId },
                        principalTable: "report_pages",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_report_cards_AccountId_ReportPageId_Id",
                table: "report_cards",
                columns: new[] { "AccountId", "ReportPageId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_cards_AccountId_ReportPageId_SortOrder",
                table: "report_cards",
                columns: new[] { "AccountId", "ReportPageId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_report_pages_AccountId_Name",
                table: "report_pages",
                columns: new[] { "AccountId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_report_pages_AccountId_OwnerUserId",
                table: "report_pages",
                columns: new[] { "AccountId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_report_pages_CreatedByUserId",
                table: "report_pages",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_report_pages_OwnerUserId",
                table: "report_pages",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_report_pages_UpdatedByUserId",
                table: "report_pages",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_cards");

            migrationBuilder.DropTable(
                name: "report_pages");
        }
    }
}
