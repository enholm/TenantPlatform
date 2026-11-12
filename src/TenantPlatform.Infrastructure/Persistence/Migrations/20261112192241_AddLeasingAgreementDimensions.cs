using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeasingAgreementDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leasing_acquisition_dimension_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValueId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DimensionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ValueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ValueCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ValuePath = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_acquisition_dimension_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leasing_acquisition_dimension_assignments_dimension_values_~",
                        columns: x => new { x.AccountId, x.DimensionId, x.ValueId },
                        principalTable: "dimension_values",
                        principalColumns: new[] { "AccountId", "DimensionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_acquisition_dimension_assignments_leasing_acquisiti~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_agreement_dimension_rules",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_agreement_dimension_rules", x => new { x.AccountId, x.DimensionId });
                    table.CheckConstraint("CK_leasing_agreement_dimension_rule_required", "NOT \"IsRequired\" OR \"IsEnabled\"");
                    table.ForeignKey(
                        name: "FK_leasing_agreement_dimension_rules_dimensions_AccountId_Dime~",
                        columns: x => new { x.AccountId, x.DimensionId },
                        principalTable: "dimensions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_framework_dimension_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValueId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DimensionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ValueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ValueCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ValuePath = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_framework_dimension_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leasing_framework_dimension_assignments_dimension_values_Ac~",
                        columns: x => new { x.AccountId, x.DimensionId, x.ValueId },
                        principalTable: "dimension_values",
                        principalColumns: new[] { "AccountId", "DimensionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_framework_dimension_assignments_leasing_frameworks_~",
                        columns: x => new { x.AccountId, x.FrameworkId },
                        principalTable: "leasing_frameworks",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_acquisition_dimension_assignments_AccountId_Acquisi~",
                table: "leasing_acquisition_dimension_assignments",
                columns: new[] { "AccountId", "AcquisitionId", "DimensionId", "ValueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leasing_acquisition_dimension_assignments_AccountId_Dimensi~",
                table: "leasing_acquisition_dimension_assignments",
                columns: new[] { "AccountId", "DimensionId", "ValueId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_framework_dimension_assignments_AccountId_Dimension~",
                table: "leasing_framework_dimension_assignments",
                columns: new[] { "AccountId", "DimensionId", "ValueId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_framework_dimension_assignments_AccountId_Framework~",
                table: "leasing_framework_dimension_assignments",
                columns: new[] { "AccountId", "FrameworkId", "DimensionId", "ValueId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leasing_acquisition_dimension_assignments");

            migrationBuilder.DropTable(
                name: "leasing_agreement_dimension_rules");

            migrationBuilder.DropTable(
                name: "leasing_framework_dimension_assignments");
        }
    }
}
