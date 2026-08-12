using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CondotifyAPI.Tests;

public sealed class VisitFacialInviteSecurityTests
{
    [Fact]
    public void TokenHash_IsDeterministicAndDoesNotExposeRawToken()
    {
        using var context = CreateContext();
        var service = new VisitFacialInviteService(context, new ConfigurationBuilder().Build());
        const string rawToken = "convite-facial-muito-secreto-123";

        var first = service.HashToken(rawToken);
        var second = service.HashToken(rawToken);
        var different = service.HashToken(rawToken + "x");

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain(rawToken, first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InviteModel_UsesUniqueTokenAndOneInvitePerVisit()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(VisitFacialInviteDTO));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(VisitFacialInviteDTO.TokenHash)]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(VisitFacialInviteDTO.VisitId)]));
        Assert.NotNull(entity.GetQueryFilter());
    }

    private static DatabaseContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=localhost;Database=Condotify_ModelOnly;Username=postgres;Password=postgres")
            .Options;
        return new DatabaseContext(options);
    }
}
