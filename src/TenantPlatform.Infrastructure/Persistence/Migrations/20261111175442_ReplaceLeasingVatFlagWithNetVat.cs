using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceLeasingVatFlagWithNetVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_leasing_framework_limit",
                table: "leasing_frameworks");

            migrationBuilder.RenameColumn(
                name: "PreviousLimit",
                table: "leasing_limit_changes",
                newName: "PreviousLimitNet");

            migrationBuilder.RenameColumn(
                name: "NewLimit",
                table: "leasing_limit_changes",
                newName: "NewLimitNet");

            migrationBuilder.RenameColumn(
                name: "Limit",
                table: "leasing_frameworks",
                newName: "LimitNet");

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousLimitVat",
                table: "leasing_limit_changes",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NewLimitVat",
                table: "leasing_limit_changes",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LimitVat",
                table: "leasing_frameworks",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE leasing_frameworks
                SET "LimitVat" = CASE
                    WHEN "IncludesVat" THEN "LimitNet" - round("LimitNet" / 1.25, 2)
                    ELSE round("LimitNet" * 0.25, 2)
                END,
                "LimitNet" = CASE
                    WHEN "IncludesVat" THEN round("LimitNet" / 1.25, 2)
                    ELSE "LimitNet"
                END;

                UPDATE leasing_limit_changes AS c
                SET "PreviousLimitVat" = CASE
                        WHEN f."IncludesVat" THEN c."PreviousLimitNet" - round(c."PreviousLimitNet" / 1.25, 2)
                        ELSE round(c."PreviousLimitNet" * 0.25, 2)
                    END,
                    "PreviousLimitNet" = CASE
                        WHEN f."IncludesVat" THEN round(c."PreviousLimitNet" / 1.25, 2)
                        ELSE c."PreviousLimitNet"
                    END,
                    "NewLimitVat" = CASE
                        WHEN f."IncludesVat" THEN c."NewLimitNet" - round(c."NewLimitNet" / 1.25, 2)
                        ELSE round(c."NewLimitNet" * 0.25, 2)
                    END,
                    "NewLimitNet" = CASE
                        WHEN f."IncludesVat" THEN round(c."NewLimitNet" / 1.25, 2)
                        ELSE c."NewLimitNet"
                    END
                FROM leasing_frameworks AS f
                WHERE f."AccountId" = c."AccountId" AND f."Id" = c."FrameworkId";
                """);

            migrationBuilder.DropColumn(
                name: "IncludesVat",
                table: "leasing_frameworks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_leasing_framework_limit",
                table: "leasing_frameworks",
                sql: "\"LimitNet\" >= 0 AND \"LimitVat\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_leasing_framework_limit",
                table: "leasing_frameworks");

            migrationBuilder.AddColumn<bool>(
                name: "IncludesVat",
                table: "leasing_frameworks",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""
                UPDATE leasing_frameworks
                SET "LimitNet" = "LimitNet" + "LimitVat";

                UPDATE leasing_limit_changes
                SET "PreviousLimitNet" = "PreviousLimitNet" + "PreviousLimitVat",
                    "NewLimitNet" = "NewLimitNet" + "NewLimitVat";
                """);

            migrationBuilder.DropColumn(
                name: "PreviousLimitVat",
                table: "leasing_limit_changes");

            migrationBuilder.DropColumn(
                name: "NewLimitVat",
                table: "leasing_limit_changes");

            migrationBuilder.DropColumn(
                name: "LimitVat",
                table: "leasing_frameworks");

            migrationBuilder.RenameColumn(
                name: "PreviousLimitNet",
                table: "leasing_limit_changes",
                newName: "PreviousLimit");

            migrationBuilder.RenameColumn(
                name: "NewLimitNet",
                table: "leasing_limit_changes",
                newName: "NewLimit");

            migrationBuilder.RenameColumn(
                name: "LimitNet",
                table: "leasing_frameworks",
                newName: "Limit");

            migrationBuilder.AddCheckConstraint(
                name: "CK_leasing_framework_limit",
                table: "leasing_frameworks",
                sql: "\"Limit\" >= 0");
        }
    }
}
