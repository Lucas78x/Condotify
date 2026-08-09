using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext : DbContext
{
    private readonly CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor _tenant;

    public DatabaseContext() => _tenant = CondotifyAPI.Domain.Services.NullCurrentTenantAccessor.Instance;

    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options) =>
        _tenant = CondotifyAPI.Domain.Services.NullCurrentTenantAccessor.Instance;

    public DatabaseContext(DbContextOptions<DatabaseContext> options, CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor tenant) : base(options) =>
        _tenant = tenant;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql(GetDefaultConnectionString());
        }

        base.OnConfiguring(optionsBuilder);
    }

    public static string GetDefaultConnectionString()
    {
        return Environment.GetEnvironmentVariable("CONDOTIFY_DB_CONNECTION")
            ?? "Server=localhost;Database=Condotify;User Id=postgres;Pwd=postgres";
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        EnterprisesEntityConfiguration(modelBuilder);
        UsersEntityConfiguration(modelBuilder);
        LicensesEntityConfiguration(modelBuilder);
        LicenseAdministrationEntityConfiguration(modelBuilder);
        DevicesEntityConfiguration(modelBuilder);
        AccessRoutesEntityConfiguration(modelBuilder);
        AmenitiesEntityConfiguration(modelBuilder);
        DeliveriesEntityConfiguration(modelBuilder); 
        TicketsEntityConfiguration(modelBuilder);
        BlocksEntityConfiguration(modelBuilder);
        UnitsEntityConfiguration(modelBuilder);
        ResidentsEntityConfiguration(modelBuilder);
        AuditsEntityConfiguration(modelBuilder);
        RecycleBinEntityConfiguration(modelBuilder);
        BackupsEntityConfiguration(modelBuilder);
        ObservabilityEntityConfiguration(modelBuilder);
        RefreshTokensEntityConfiguration(modelBuilder);
        MobileEntityConfiguration(modelBuilder);
        SafetyOperationsEntityConfiguration(modelBuilder);
        BoletoEntityConfiguration(modelBuilder);
        ResourceDocumentEntityConfiguration(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    property.SetColumnType("timestamp with time zone");

                if (property.Name == "CreatedAt" && property.ClrType == typeof(DateTime))
                    property.SetDefaultValueSql("NOW()");
            }
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(CondotifyAPI.Domain.Interfaces.ILicenseScoped).IsAssignableFrom(entityType.ClrType)) continue;
            var method = SetLicenseScopedFilterMethod.MakeGenericMethod(entityType.ClrType);
            method.Invoke(this, [modelBuilder]);
        }
    }

    private static readonly System.Reflection.MethodInfo SetLicenseScopedFilterMethod =
        typeof(DatabaseContext).GetMethod(nameof(SetLicenseScopedFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private void SetLicenseScopedFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, CondotifyAPI.Domain.Interfaces.ILicenseScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(x =>
            _tenant.AccessibleLicenseIds != null && _tenant.AccessibleLicenseIds.Contains(x.LicenseId));
    }
}
