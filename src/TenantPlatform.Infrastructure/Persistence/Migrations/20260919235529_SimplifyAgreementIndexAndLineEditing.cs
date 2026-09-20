using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyAgreementIndexAndLineEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agreement_line_versions_agreement_delivery_groups_AccountId~",
                table: "agreement_line_versions");

            migrationBuilder.DropTable(
                name: "agreement_delivery_groups");

            migrationBuilder.DropIndex(
                name: "IX_agreement_line_versions_AccountId_AgreementId_DeliveryGroup~",
                table: "agreement_line_versions");

            migrationBuilder.DropColumn(
                name: "DeliveryGroupId",
                table: "agreement_line_versions");

            migrationBuilder.AddColumn<Guid>(
                name: "IndexId",
                table: "agreements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IndexSetupNeedsReview",
                table: "agreements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IndexRegulated",
                table: "agreement_line_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PeriodKey",
                table: "agreement_index_values",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "Superseded",
                table: "agreement_index_values",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "RuleId",
                table: "agreement_adjustment_proposals",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "IndexId",
                table: "agreement_adjustment_proposals",
                type: "uuid",
                nullable: true);

            // Only current prices/settings are migrated. Historical rules and calculation JSON stay immutable.
            migrationBuilder.Sql("""
                UPDATE agreement_index_values v SET "PeriodKey" = first."Id"
                FROM (SELECT DISTINCT ON ("AccountId", "IndexId", "Period") "AccountId", "IndexId", "Period", "Id"
                      FROM agreement_index_values ORDER BY "AccountId", "IndexId", "Period", "Revision") first
                WHERE v."AccountId" = first."AccountId" AND v."IndexId" = first."IndexId" AND v."Period" = first."Period";

                WITH latest AS (
                    SELECT DISTINCT ON ("AccountId", "AgreementId", "LineId") *
                    FROM agreement_adjustment_rules ORDER BY "AccountId", "AgreementId", "LineId", "EffectiveFrom" DESC
                )
                UPDATE agreement_line_versions v SET "IndexRegulated" = (r."Kind" = 1)
                FROM latest r WHERE v."AccountId" = r."AccountId" AND v."AgreementId" = r."AgreementId" AND v."LineId" = r."LineId";

                WITH latest AS (
                    SELECT DISTINCT ON ("AccountId", "AgreementId", "LineId") *
                    FROM agreement_adjustment_rules ORDER BY "AccountId", "AgreementId", "LineId", "EffectiveFrom" DESC
                ), summary AS (
                    SELECT r."AccountId", r."AgreementId",
                           COUNT(DISTINCT r."IndexId") FILTER (WHERE r."Kind" = 1) AS indices,
                           (ARRAY_AGG(r."IndexId") FILTER (WHERE r."Kind" = 1))[1] AS index_id,
                           BOOL_OR(r."Kind" IN (2,3) OR (r."Kind" = 1 AND
                               (r."IndexId" IS NULL OR r."SharePercent" <> 100 OR r."Addition" <> 0 OR r."AdditionPercent" <> 0 OR
                                r."FixedPercent" <> 0 OR r."FloorPercent" IS NOT NULL OR r."CeilingPercent" IS NOT NULL OR
                                NOT r."AllowDecrease" OR r."Basis" <> 2 OR r."ComparisonOffsetMonths" <> 0 OR
                                r."PriceTiming" <> 2 OR r."IntervalMonths" <> i."Resolution" OR r."Anchor" <> 1 OR
                                r."AnchorDate" <> r."FirstAllowedDate" OR EXTRACT(DAY FROM r."FirstAllowedDate") <> 1 OR
                                MOD(EXTRACT(MONTH FROM r."FirstAllowedDate")::int - 1, i."Resolution") <> 0 OR
                                r."BaseIndexPeriod" IS DISTINCT FROM (SELECT MAX(v."Period") FROM agreement_index_values v
                                    WHERE v."AccountId" = r."AccountId" AND v."IndexId" = r."IndexId" AND v."Period" < r."FirstAllowedDate")))) AS advanced
                    FROM latest r LEFT JOIN agreement_indices i ON i."AccountId" = r."AccountId" AND i."Id" = r."IndexId"
                    GROUP BY r."AccountId", r."AgreementId"
                )
                UPDATE agreements a SET "IndexId" = CASE WHEN s.indices = 1 THEN s.index_id ELSE NULL END,
                    "IndexSetupNeedsReview" = (s.indices > 1 OR s.advanced)
                FROM summary s WHERE a."AccountId" = s."AccountId" AND a."Id" = s."AgreementId";

                -- Future legacy schedules need explicit review as they no longer drive calculations.
                UPDATE agreements a SET "IndexSetupNeedsReview" = true
                WHERE EXISTS (SELECT 1 FROM agreement_adjustment_rules r WHERE r."AccountId" = a."AccountId" AND r."AgreementId" = a."Id" AND r."EffectiveFrom" > CURRENT_DATE);
                UPDATE agreement_adjustment_proposals SET "Status" = 3 WHERE "Status" = 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_agreements_AccountId_IndexId",
                table: "agreements",
                columns: new[] { "AccountId", "IndexId" });

            migrationBuilder.AddForeignKey(
                name: "FK_agreements_agreement_indices_AccountId_IndexId",
                table: "agreements",
                columns: new[] { "AccountId", "IndexId" },
                principalTable: "agreement_indices",
                principalColumns: new[] { "AccountId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("This data-preserving upgrade cannot restore removed delivery groups or old adjustment configuration. Restore a pre-upgrade backup instead.");
        }
    }
}
