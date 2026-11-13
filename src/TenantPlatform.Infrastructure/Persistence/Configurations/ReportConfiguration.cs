using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Reports;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public sealed class ReportPageConfiguration : IEntityTypeConfiguration<ReportPage>
{
    public void Configure(EntityTypeBuilder<ReportPage> b)
    {
        b.ToTable("report_pages");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.OwnerUserId });
        b.HasIndex(x => new { x.AccountId, x.Name });
    }
}

public sealed class ReportCardConfiguration : IEntityTypeConfiguration<ReportCard>
{
    public void Configure(EntityTypeBuilder<ReportCard> b)
    {
        b.ToTable("report_cards", t =>
        {
            t.HasCheckConstraint("CK_report_cards_module", "\"Module\" IN (0,1)");
            t.HasCheckConstraint("CK_report_cards_period_rule", "\"PeriodRule\" BETWEEN 0 AND 17");
            t.HasCheckConstraint("CK_report_cards_custom_period", "(\"PeriodRule\" = 17 AND \"CustomFrom\" IS NOT NULL AND \"CustomTo\" IS NOT NULL AND \"CustomFrom\" <= \"CustomTo\") OR (\"PeriodRule\" <> 17 AND \"CustomFrom\" IS NULL AND \"CustomTo\" IS NULL)");
            t.HasCheckConstraint("CK_report_cards_definition_version", "\"DefinitionVersion\" > 0");
            t.HasCheckConstraint("CK_report_cards_grouping_kind", "\"GroupingKind\" IN (0,1,2,3)");
            t.HasCheckConstraint("CK_report_cards_grouping_shape", "(\"GroupingKind\" = 0 AND \"GroupingKey\" IS NULL AND \"GroupingScope\" IS NULL AND \"GroupingDimensionId\" IS NULL AND \"TimeGrouping\" IS NULL) OR (\"GroupingKind\" = 1 AND \"GroupingKey\" IS NULL AND \"GroupingScope\" IS NULL AND \"GroupingDimensionId\" IS NULL AND \"TimeGrouping\" IS NOT NULL) OR (\"GroupingKind\" = 2 AND \"GroupingKey\" IS NOT NULL AND \"GroupingScope\" IS NULL AND \"GroupingDimensionId\" IS NULL AND \"TimeGrouping\" IS NULL) OR (\"GroupingKind\" = 3 AND \"GroupingKey\" IS NULL AND \"GroupingScope\" IS NOT NULL AND \"GroupingDimensionId\" IS NOT NULL AND \"TimeGrouping\" IS NULL)");
            t.HasCheckConstraint("CK_report_cards_time_grouping", "\"TimeGrouping\" IS NULL OR \"TimeGrouping\" BETWEEN 0 AND 4");
            t.HasCheckConstraint("CK_report_cards_visualization", "\"Visualization\" IS NULL OR \"Visualization\" BETWEEN 0 AND 5");
            t.HasCheckConstraint("CK_report_cards_layout", "\"GridColumn\" BETWEEN 1 AND 12 AND \"GridRow\" > 0 AND \"GridWidth\" BETWEEN 1 AND 12 AND \"GridHeight\" > 0 AND \"SortOrder\" >= 0");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.FiltersJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.DisplayJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.DateBasisId).HasMaxLength(100);
        b.Property(x => x.MetricId).HasMaxLength(100);
        b.Property(x => x.AggregationId).HasMaxLength(100);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ReportPage>().WithMany().HasForeignKey(x => new { x.AccountId, x.ReportPageId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.ReportPageId, x.Id }).IsUnique();
        b.HasIndex(x => new { x.AccountId, x.ReportPageId, x.SortOrder });
    }
}
