using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext : DbContext
{
    public DatabaseContext() { }

    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options) { }

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
        DeliveriesEntityConfiguration(modelBuilder); 
        TicketsEntityConfiguration(modelBuilder);
        BlocksEntityConfiguration(modelBuilder);
        UnitsEntityConfiguration(modelBuilder);
        ResidentsEntityConfiguration(modelBuilder);
        AuditsEntityConfiguration(modelBuilder);

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
    }
}
