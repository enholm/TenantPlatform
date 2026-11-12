using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agreement_dimension_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValueId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_dimension_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_dimension_assignments_agreements_AccountId_Agreem~",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_dimension_assignments_dimension_values_AccountId_~",
                        columns: x => new { x.AccountId, x.DimensionId, x.ValueId },
                        principalTable: "dimension_values",
                        principalColumns: new[] { "AccountId", "DimensionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_dimension_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_dimension_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_dimension_history_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_dimension_history_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_dimension_rules",
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
                    table.PrimaryKey("PK_agreement_dimension_rules", x => new { x.AccountId, x.DimensionId });
                    table.CheckConstraint("CK_agreement_dimension_required_enabled", "NOT \"IsRequired\" OR \"IsEnabled\"");
                    table.ForeignKey(
                        name: "FK_agreement_dimension_rules_dimensions_AccountId_DimensionId",
                        columns: x => new { x.AccountId, x.DimensionId },
                        principalTable: "dimensions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_dimension_assignments_AccountId_AgreementId_Dimen~",
                table: "agreement_dimension_assignments",
                columns: new[] { "AccountId", "AgreementId", "DimensionId", "ValueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_dimension_assignments_AccountId_DimensionId_Value~",
                table: "agreement_dimension_assignments",
                columns: new[] { "AccountId", "DimensionId", "ValueId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_dimension_history_AccountId_AgreementId_RecordedU~",
                table: "agreement_dimension_history",
                columns: new[] { "AccountId", "AgreementId", "RecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_dimension_history_ActorUserId",
                table: "agreement_dimension_history",
                column: "ActorUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_dimension_assignments");

            migrationBuilder.DropTable(
                name: "agreement_dimension_history");

            migrationBuilder.DropTable(
                name: "agreement_dimension_rules");
        }
    }
}
