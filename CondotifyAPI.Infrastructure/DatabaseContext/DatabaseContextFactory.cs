using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CondotifyAPI.Infrastructure;

public class DatabaseContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
{
    public DatabaseContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql(DatabaseContext.GetDefaultConnectionString())
            .Options;

        return new DatabaseContext(options);
    }
}
