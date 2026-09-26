using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedDimensionsAndLeasingAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AllocationMode",
                table: "leasing_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_leasing_items_AccountId_Id",
                table: "leasing_items",
                columns: new[] { "AccountId", "Id" });

            migrationBuilder.CreateTable(
                name: "dimensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AllowMultiple = table.Column<bool>(type: "boolean", nullable: false),
                    LeafOnly = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dimensions", x => x.Id);
                    table.UniqueConstraint("AK_dimensions_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_dimensions_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_allocation_rows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    InputValue = table.Column<decimal>(type: "numeric(24,4)", precision: 24, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_allocation_rows", x => x.Id);
                    table.UniqueConstraint("AK_leasing_allocation_rows_AccountId_ItemId_Id", x => new { x.AccountId, x.ItemId, x.Id });
                    table.CheckConstraint("CK_leasing_allocation_nonnegative", "\"InputValue\" >= 0");
                    table.ForeignKey(
                        name: "FK_leasing_allocation_rows_leasing_items_AccountId_ItemId",
                        columns: x => new { x.AccountId, x.ItemId },
                        principalTable: "leasing_items",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dimension_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dimension_values", x => x.Id);
                    table.UniqueConstraint("AK_dimension_values_AccountId_DimensionId_Id", x => new { x.AccountId, x.DimensionId, x.Id });
                    table.ForeignKey(
                        name: "FK_dimension_values_dimension_values_AccountId_DimensionId_Par~",
                        columns: x => new { x.AccountId, x.DimensionId, x.ParentId },
                        principalTable: "dimension_values",
                        principalColumns: new[] { "AccountId", "DimensionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dimension_values_dimensions_AccountId_DimensionId",
                        columns: x => new { x.AccountId, x.DimensionId },
                        principalTable: "dimensions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_allocation_dimensions",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DimensionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_allocation_dimensions", x => new { x.AccountId, x.ItemId, x.DimensionId });
                    table.ForeignKey(
                        name: "FK_leasing_allocation_dimensions_dimensions_AccountId_Dimensio~",
                        columns: x => new { x.AccountId, x.DimensionId },
                        principalTable: "dimensions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_allocation_dimensions_leasing_items_AccountId_ItemId",
                        columns: x => new { x.AccountId, x.ItemId },
                        principalTable: "leasing_items",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_dimension_rules",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    AllowAllocation = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_dimension_rules", x => new { x.AccountId, x.DimensionId });
                    table.ForeignKey(
                        name: "FK_leasing_dimension_rules_dimensions_AccountId_DimensionId",
                        columns: x => new { x.AccountId, x.DimensionId },
                        principalTable: "dimensions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dimension_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValueId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dimension_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dimension_history_dimension_values_AccountId_DimensionId_Va~",
                        columns: x => new { x.AccountId, x.DimensionId, x.ValueId },
                        principalTable: "dimension_values",
                        principalColumns: new[] { "AccountId", "DimensionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dimension_history_dimensions_AccountId_DimensionId",
                        columns: x => new { x.AccountId, x.DimensionId },
                        principalTable: "dimensions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dimension_history_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_dimension_selections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValueId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRowId = table.Column<Guid>(type: "uuid", nullable: true),
                    DimensionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DimensionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ValueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ValueCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ValuePath = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_dimension_selections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leasing_dimension_selections_dimension_values_AccountId_Dim~",
                        columns: x => new { x.AccountId, x.DimensionId, x.ValueId },
                        principalTable: "dimension_values",
                        principalColumns: new[] { "AccountId", "DimensionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_dimension_selections_leasing_allocation_rows_Accoun~",
                        columns: x => new { x.AccountId, x.ItemId, x.AllocationRowId },
                        principalTable: "leasing_allocation_rows",
                        principalColumns: new[] { "AccountId", "ItemId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_dimension_selections_leasing_items_AccountId_ItemId",
                        columns: x => new { x.AccountId, x.ItemId },
                        principalTable: "leasing_items",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dimension_history_AccountId_DimensionId_ValueId",
                table: "dimension_history",
                columns: new[] { "AccountId", "DimensionId", "ValueId" });

            migrationBuilder.CreateIndex(
                name: "IX_dimension_history_ActorUserId",
                table: "dimension_history",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_dimension_values_AccountId_DimensionId_Code",
                table: "dimension_values",
                columns: new[] { "AccountId", "DimensionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dimension_values_AccountId_DimensionId_ParentId",
                table: "dimension_values",
                columns: new[] { "AccountId", "DimensionId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_dimensions_AccountId_Code",
                table: "dimensions",
                columns: new[] { "AccountId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leasing_allocation_dimensions_AccountId_DimensionId",
                table: "leasing_allocation_dimensions",
                columns: new[] { "AccountId", "DimensionId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_dimension_selections_AccountId_DimensionId_ValueId",
                table: "leasing_dimension_selections",
                columns: new[] { "AccountId", "DimensionId", "ValueId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_dimension_selections_AccountId_ItemId_AllocationRow~",
                table: "leasing_dimension_selections",
                columns: new[] { "AccountId", "ItemId", "AllocationRowId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dimension_history");

            migrationBuilder.DropTable(
                name: "leasing_allocation_dimensions");

            migrationBuilder.DropTable(
                name: "leasing_dimension_rules");

            migrationBuilder.DropTable(
                name: "leasing_dimension_selections");

            migrationBuilder.DropTable(
                name: "dimension_values");

            migrationBuilder.DropTable(
                name: "leasing_allocation_rows");

            migrationBuilder.DropTable(
                name: "dimensions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_leasing_items_AccountId_Id",
                table: "leasing_items");

            migrationBuilder.DropColumn(
                name: "AllocationMode",
                table: "leasing_items");
        }
    }
}
