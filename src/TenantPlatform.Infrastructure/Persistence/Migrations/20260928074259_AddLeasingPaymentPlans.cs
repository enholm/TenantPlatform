using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeasingPaymentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "leasing_invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "FinanceOrganizationId",
                table: "leasing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalInvoiceId",
                table: "leasing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_AdvanceRent",
                table: "leasing_frameworks",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms_DocumentReference",
                table: "leasing_frameworks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_EstablishmentFee",
                table: "leasing_frameworks",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms_FinanceReference",
                table: "leasing_frameworks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms_FinancingNotes",
                table: "leasing_frameworks",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Terms_FirstDueDate",
                table: "leasing_frameworks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Terms_ObservationDate",
                table: "leasing_frameworks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_ObservedReferenceRate",
                table: "leasing_frameworks",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_OtherFees",
                table: "leasing_frameworks",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Terms_PaymentTiming",
                table: "leasing_frameworks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_RateCap",
                table: "leasing_frameworks",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_RateFloor",
                table: "leasing_frameworks",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Terms_RateLimitBasis",
                table: "leasing_frameworks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Terms_RateResetFrequency",
                table: "leasing_frameworks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms_ResidualDocumentReference",
                table: "leasing_frameworks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Terms_ResidualIsObligation",
                table: "leasing_frameworks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_ResidualValue",
                table: "leasing_frameworks",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_AdvanceRent",
                table: "leasing_acquisitions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms_DocumentReference",
                table: "leasing_acquisitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_EstablishmentFee",
                table: "leasing_acquisitions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms_FinanceReference",
                table: "leasing_acquisitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms_FinancingNotes",
                table: "leasing_acquisitions",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Terms_FirstDueDate",
                table: "leasing_acquisitions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Terms_ObservationDate",
                table: "leasing_acquisitions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_ObservedReferenceRate",
                table: "leasing_acquisitions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_OtherFees",
                table: "leasing_acquisitions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Terms_PaymentTiming",
                table: "leasing_acquisitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_RateCap",
                table: "leasing_acquisitions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_RateFloor",
                table: "leasing_acquisitions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Terms_RateLimitBasis",
                table: "leasing_acquisitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Terms_RateResetFrequency",
                table: "leasing_acquisitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms_ResidualDocumentReference",
                table: "leasing_acquisitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Terms_ResidualIsObligation",
                table: "leasing_acquisitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Terms_ResidualValue",
                table: "leasing_acquisitions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "leasing_financing_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_financing_revisions", x => x.Id);
                    table.UniqueConstraint("AK_leasing_financing_revisions_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_financing_revisions_leasing_acquisitions_AccountId_~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_installments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BillingComplete = table.Column<bool>(type: "boolean", nullable: false),
                    VarianceAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    ControlReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CheckedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_installments", x => x.Id);
                    table.UniqueConstraint("AK_leasing_installments_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_installments_leasing_acquisitions_AccountId_Acquisi~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_payment_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_payment_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leasing_payment_events_leasing_acquisitions_AccountId_Acqui~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_payment_events_leasing_invoices_AccountId_InvoiceId",
                        columns: x => new { x.AccountId, x.InvoiceId },
                        principalTable: "leasing_invoices",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_payment_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BasedOnPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_payment_plans", x => x.Id);
                    table.UniqueConstraint("AK_leasing_payment_plans_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_payment_plans_leasing_acquisitions_AccountId_Acquis~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_payment_plans_leasing_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "leasing_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_payment_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditedAllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Net = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Reversed = table.Column<bool>(type: "boolean", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_payment_allocations", x => x.Id);
                    table.UniqueConstraint("AK_leasing_payment_allocations_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.CheckConstraint("CK_payment_allocation_positive", "\"Net\" >= 0 AND \"Vat\" >= 0 AND \"Net\" + \"Vat\" > 0");
                    table.ForeignKey(
                        name: "FK_leasing_payment_allocations_leasing_installments_AccountId_~",
                        columns: x => new { x.AccountId, x.InstallmentId },
                        principalTable: "leasing_installments",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_payment_allocations_leasing_invoices_AccountId_Invo~",
                        columns: x => new { x.AccountId, x.InvoiceId },
                        principalTable: "leasing_invoices",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_payment_allocations_leasing_payment_allocations_Acc~",
                        columns: x => new { x.AccountId, x.CreditedAllocationId },
                        principalTable: "leasing_payment_allocations",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_plan_terms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Net = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    Vat = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    Gross = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    CapitalComponent = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    InterestComponent = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    FeeComponent = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    ComponentsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    FinancingRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    NeedsReview = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ObligationDocumentReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_plan_terms", x => x.Id);
                    table.CheckConstraint("CK_payment_term_totals", "(\"Net\" IS NULL OR \"Net\" >= 0) AND (\"Vat\" IS NULL OR \"Vat\" >= 0) AND (\"Gross\" IS NULL OR \"Gross\" >= 0) AND (\"Net\" IS NULL OR \"Vat\" IS NULL OR \"Gross\" IS NULL OR \"Gross\" = \"Net\" + \"Vat\")");
                    table.ForeignKey(
                        name: "FK_leasing_plan_terms_leasing_financing_revisions_AccountId_Fi~",
                        columns: x => new { x.AccountId, x.FinancingRevisionId },
                        principalTable: "leasing_financing_revisions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_plan_terms_leasing_installments_AccountId_Installme~",
                        columns: x => new { x.AccountId, x.InstallmentId },
                        principalTable: "leasing_installments",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_plan_terms_leasing_payment_plans_AccountId_PlanId",
                        columns: x => new { x.AccountId, x.PlanId },
                        principalTable: "leasing_payment_plans",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoices_AccountId_Category_Status",
                table: "leasing_invoices",
                columns: new[] { "AccountId", "Category", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoices_AccountId_OriginalInvoiceId",
                table: "leasing_invoices",
                columns: new[] { "AccountId", "OriginalInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_financing_revisions_AccountId_AcquisitionId",
                table: "leasing_financing_revisions",
                columns: new[] { "AccountId", "AcquisitionId" },
                unique: true,
                filter: "\"EffectiveFrom\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_financing_revisions_AccountId_AcquisitionId_Effecti~",
                table: "leasing_financing_revisions",
                columns: new[] { "AccountId", "AcquisitionId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leasing_installments_AccountId_AcquisitionId_Reference",
                table: "leasing_installments",
                columns: new[] { "AccountId", "AcquisitionId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leasing_payment_allocations_AccountId_CreditedAllocationId",
                table: "leasing_payment_allocations",
                columns: new[] { "AccountId", "CreditedAllocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_payment_allocations_AccountId_InstallmentId",
                table: "leasing_payment_allocations",
                columns: new[] { "AccountId", "InstallmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_payment_allocations_AccountId_InvoiceId",
                table: "leasing_payment_allocations",
                columns: new[] { "AccountId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_payment_events_AccountId_AcquisitionId",
                table: "leasing_payment_events",
                columns: new[] { "AccountId", "AcquisitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_payment_events_AccountId_InvoiceId",
                table: "leasing_payment_events",
                columns: new[] { "AccountId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_payment_plans_AccountId_AcquisitionId",
                table: "leasing_payment_plans",
                columns: new[] { "AccountId", "AcquisitionId" },
                unique: true,
                filter: "\"Status\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_payment_plans_AccountId_AcquisitionId_Version",
                table: "leasing_payment_plans",
                columns: new[] { "AccountId", "AcquisitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leasing_payment_plans_SourceDocumentId",
                table: "leasing_payment_plans",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_plan_terms_AccountId_FinancingRevisionId",
                table: "leasing_plan_terms",
                columns: new[] { "AccountId", "FinancingRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_plan_terms_AccountId_InstallmentId",
                table: "leasing_plan_terms",
                columns: new[] { "AccountId", "InstallmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_plan_terms_AccountId_PlanId_InstallmentId",
                table: "leasing_plan_terms",
                columns: new[] { "AccountId", "PlanId", "InstallmentId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_leasing_invoices_leasing_invoices_AccountId_OriginalInvoice~",
                table: "leasing_invoices",
                columns: new[] { "AccountId", "OriginalInvoiceId" },
                principalTable: "leasing_invoices",
                principalColumns: new[] { "AccountId", "Id" },
                onDelete: ReferentialAction.Restrict);
            // Preserve original financing with unknown effective date; no generated plans or changed purchase amounts.
            migrationBuilder.Sql("""
                INSERT INTO leasing_financing_revisions ("Id", "AccountId", "AcquisitionId", "EffectiveFrom", "SnapshotJson", "ActorUserId", "RecordedUtc", "Reason")
                SELECT md5(a."Id"::text || ':financing-baseline')::uuid, a."AccountId", a."Id", NULL,
                    jsonb_build_object('FinanceOrganizationId', a."FinanceOrganizationId", 'Currency', a."Currency", 'FinancedAmount', a."FinancedAmount", 'Terms',
                      (SELECT jsonb_object_agg(substring(key from 7), value) FROM jsonb_each(to_jsonb(a)) WHERE key LIKE 'Terms_%')),
                    NULL, NULL, 'PaymentInitialUnknownDate'
                FROM leasing_acquisitions a;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_leasing_invoices_leasing_invoices_AccountId_OriginalInvoice~",
                table: "leasing_invoices");

            migrationBuilder.DropTable(
                name: "leasing_payment_allocations");

            migrationBuilder.DropTable(
                name: "leasing_payment_events");

            migrationBuilder.DropTable(
                name: "leasing_plan_terms");

            migrationBuilder.DropTable(
                name: "leasing_financing_revisions");

            migrationBuilder.DropTable(
                name: "leasing_installments");

            migrationBuilder.DropTable(
                name: "leasing_payment_plans");

            migrationBuilder.DropIndex(
                name: "IX_leasing_invoices_AccountId_Category_Status",
                table: "leasing_invoices");

            migrationBuilder.DropIndex(
                name: "IX_leasing_invoices_AccountId_OriginalInvoiceId",
                table: "leasing_invoices");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "leasing_invoices");

            migrationBuilder.DropColumn(
                name: "FinanceOrganizationId",
                table: "leasing_invoices");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceId",
                table: "leasing_invoices");

            migrationBuilder.DropColumn(
                name: "Terms_AdvanceRent",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_DocumentReference",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_EstablishmentFee",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_FinanceReference",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_FinancingNotes",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_FirstDueDate",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_ObservationDate",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_ObservedReferenceRate",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_OtherFees",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_PaymentTiming",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_RateCap",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_RateFloor",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_RateLimitBasis",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_RateResetFrequency",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_ResidualDocumentReference",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_ResidualIsObligation",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_ResidualValue",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "Terms_AdvanceRent",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_DocumentReference",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_EstablishmentFee",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_FinanceReference",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_FinancingNotes",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_FirstDueDate",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_ObservationDate",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_ObservedReferenceRate",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_OtherFees",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_PaymentTiming",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_RateCap",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_RateFloor",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_RateLimitBasis",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_RateResetFrequency",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_ResidualDocumentReference",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_ResidualIsObligation",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "Terms_ResidualValue",
                table: "leasing_acquisitions");
        }
    }
}
