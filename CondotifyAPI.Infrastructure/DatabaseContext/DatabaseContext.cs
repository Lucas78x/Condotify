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
            optionsBuilder.UseNpgsql("Server=localhost;Database=Condotify;User Id=postgres;Pwd=postgres");
        }

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    property.SetColumnType("timestamp");

                if (property.Name == "CreatedAt" && property.ClrType == typeof(DateTime))
                {
                    property.SetDefaultValueSql("NOW()");
                }
            }
        }

        EnterprisesEntityConfiguration(modelBuilder);
        UsersEntityConfiguration(modelBuilder);
        LicensesEntityConfiguration(modelBuilder);
        DevicesEntityConfiguration(modelBuilder);
        DeliveriesEntityConfiguration(modelBuilder); 
        TicketsEntityConfiguration(modelBuilder);
        BlocksEntityConfiguration(modelBuilder);
        UnitsEntityConfiguration(modelBuilder);
        ResidentsEntityConfiguration(modelBuilder);
        AuditsEntityConfiguration(modelBuilder);
    }
}