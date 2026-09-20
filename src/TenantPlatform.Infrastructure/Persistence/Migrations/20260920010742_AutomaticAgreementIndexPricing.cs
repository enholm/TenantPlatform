using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticAgreementIndexPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "IndexBaseDate",
                table: "agreement_price_versions",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agreement_index_selections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    IndexId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_index_selections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_index_selections_agreement_indices_AccountId_Inde~",
                        columns: x => new { x.AccountId, x.IndexId },
                        principalTable: "agreement_indices",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_index_selections_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_index_selections_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_index_selections_AccountId_AgreementId_Sequence",
                table: "agreement_index_selections",
                columns: new[] { "AccountId", "AgreementId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_index_selections_AccountId_IndexId",
                table: "agreement_index_selections",
                columns: new[] { "AccountId", "IndexId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_index_selections_ActorUserId",
                table: "agreement_index_selections",
                column: "ActorUserId");
            migrationBuilder.Sql("""
                INSERT INTO agreement_index_selections ("Id", "AccountId", "AgreementId", "IndexId", "Sequence", "EffectiveFrom", "RecordedUtc", "ActorUserId")
                SELECT gen_random_uuid(), "AccountId", "Id", "IndexId", 1, "StartDate", "UpdatedUtc", "UpdatedByUserId"
                FROM agreements WHERE "IndexId" IS NOT NULL;
                UPDATE agreement_adjustment_proposals SET "Status" = 3 WHERE "Status" = 0;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_index_selections");

            migrationBuilder.DropColumn(
                name: "IndexBaseDate",
                table: "agreement_price_versions");
        }
    }
}
