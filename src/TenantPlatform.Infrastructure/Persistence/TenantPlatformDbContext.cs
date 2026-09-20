using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Networking;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
using TenantPlatform.Core.Auditing;
using TenantPlatform.Core.MeetingRooms;

namespace TenantPlatform.Infrastructure.Persistence;

public class TenantPlatformDbContext : DbContext
{
    public DbSet<AgreementIndexSelection> AgreementIndexSelections => Set<AgreementIndexSelection>();
    public DbSet<AgreementIndex> AgreementIndexRecords => Set<AgreementIndex>();
    public DbSet<AgreementIndexValue> AgreementIndexValueRecords => Set<AgreementIndexValue>();
    public DbSet<AgreementAdjustmentRule> AgreementAdjustmentRuleRecords => Set<AgreementAdjustmentRule>();
    public DbSet<AgreementAdjustmentDocument> AgreementAdjustmentDocumentRecords => Set<AgreementAdjustmentDocument>();
    public DbSet<AgreementAdjustmentProposal> AgreementAdjustmentProposalRecords => Set<AgreementAdjustmentProposal>();
    public DbSet<AgreementBasis> AgreementBasisRecords => Set<AgreementBasis>();
    public DbSet<AgreementBasisSnapshot> AgreementBasisSnapshotRecords => Set<AgreementBasisSnapshot>();
    public DbSet<AgreementBasisEvent> AgreementBasisEventRecords => Set<AgreementBasisEvent>();
    public DbSet<AgreementNoticeHistory> AgreementNoticeHistory => Set<AgreementNoticeHistory>();
    public DbSet<AgreementLine> AgreementLines => Set<AgreementLine>();
    public DbSet<AgreementLineVersion> AgreementLineVersions => Set<AgreementLineVersion>();
    public DbSet<AgreementPriceVersion> AgreementPriceVersions => Set<AgreementPriceVersion>();
    public DbSet<AgreementLineDocument> AgreementLineDocuments => Set<AgreementLineDocument>();
    public DbSet<Agreement> Agreements => Set<Agreement>();
    public DbSet<AgreementDeadline> AgreementDeadlines => Set<AgreementDeadline>();
    public DbSet<AgreementDeadlineHistory> AgreementDeadlineHistory => Set<AgreementDeadlineHistory>();
    public DbSet<AgreementReminder> AgreementReminders => Set<AgreementReminder>();
    public DbSet<AgreementReminderSettings> AgreementReminderSettings => Set<AgreementReminderSettings>();
    public DbSet<AgreementAccess> AgreementAccess => Set<AgreementAccess>();
    public DbSet<AgreementDocument> AgreementDocuments => Set<AgreementDocument>();
    public TenantPlatformDbContext(
        DbContextOptions<TenantPlatformDbContext> options)
        : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Occupancy> Occupancies => Set<Occupancy>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserAccountRole> UserAccountRoles => Set<UserAccountRole>();
    public DbSet<ServiceDefinition> ServiceDefinitions => Set<ServiceDefinition>();
    public DbSet<ServiceDefinitionTranslation> ServiceDefinitionTranslations => Set<ServiceDefinitionTranslation>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<NetworkSsid> NetworkSsids => Set<NetworkSsid>();
    public DbSet<SsidRequestDetails> SsidRequestDetails => Set<SsidRequestDetails>();
    public DbSet<NetworkEnvironment> NetworkEnvironments => Set<NetworkEnvironment>();
    public DbSet<LoginAccount> LoginAccounts => Set<LoginAccount>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<ServiceDefinitionField> ServiceDefinitionFields =>
        Set<ServiceDefinitionField>();

    public DbSet<ServiceDefinitionProvider> ServiceDefinitionProviders =>
        Set<ServiceDefinitionProvider>();

    public DbSet<ServiceRequestFieldValue> ServiceRequestFieldValues =>
        Set<ServiceRequestFieldValue>();

    public DbSet<ServiceRequestMessage> ServiceRequestMessages =>
        Set<ServiceRequestMessage>();

    public DbSet<ServiceDefinitionFieldTranslation> ServiceDefinitionFieldTranslations =>
            Set<ServiceDefinitionFieldTranslation>();

    public DbSet<EmailOutboxMessage> EmailOutboxMessages =>
        Set<EmailOutboxMessage>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<MeetingRoom> MeetingRooms => Set<MeetingRoom>();
    public DbSet<RoomBooking> RoomBookings => Set<RoomBooking>();
    public DbSet<CalendarIntegration> CalendarIntegrations => Set<CalendarIntegration>();
    public DbSet<MeetingRoomCalendar> MeetingRoomCalendars => Set<MeetingRoomCalendar>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TenantPlatformDbContext).Assembly);
    }
}
