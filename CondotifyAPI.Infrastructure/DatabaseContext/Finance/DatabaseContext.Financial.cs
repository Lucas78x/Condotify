using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Infrastructure.ContextConfiguration.Finance;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<FinancialChargeDTO> FinancialCharges { get; set; }
    public DbSet<FinancialChargeEventDTO> FinancialChargeEvents { get; set; }
    public DbSet<FinancialRecurringRuleDTO> FinancialRecurringRules { get; set; }
    public DbSet<FinancialRecurringRuleUnitDTO> FinancialRecurringRuleUnits { get; set; }
    public DbSet<FinancialImportBatchDTO> FinancialImportBatches { get; set; }
    public DbSet<FinancialReminderPolicyDTO> FinancialReminderPolicies { get; set; }
    public DbSet<FinancialReminderDeliveryDTO> FinancialReminderDeliveries { get; set; }

    internal static void FinancialEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new FinancialChargeConfiguration());
        builder.ApplyConfiguration(new FinancialChargeEventConfiguration());
        builder.ApplyConfiguration(new FinancialRecurringRuleConfiguration());
        builder.ApplyConfiguration(new FinancialRecurringRuleUnitConfiguration());
        builder.ApplyConfiguration(new FinancialImportBatchConfiguration());
        builder.ApplyConfiguration(new FinancialReminderPolicyConfiguration());
        builder.ApplyConfiguration(new FinancialReminderDeliveryConfiguration());
    }
}
