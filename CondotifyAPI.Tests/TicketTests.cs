using CondotifyAPI.Domain.Models.Ticket;

namespace CondotifyAPI.Tests;

public sealed class TicketTests
{
    [Fact]
    public void Create_ShouldInitializeAuditTrail()
    {
        var ticket = CreateTicket();

        var audit = Assert.Single(ticket.Audits);
        Assert.Equal(ticket.Id, audit.TicketId);
        Assert.Equal(TicketStatusTypeEnum.Send, audit.Status);
    }

    [Fact]
    public void ExpireAndCancel_ShouldRecordStateTransitionsOnlyOnce()
    {
        var expired = CreateTicket();
        expired.Expire();
        expired.Expire();

        Assert.Equal(TicketStatusTypeEnum.Expired, expired.Status);
        Assert.Equal(2, expired.Audits.Count);

        var canceled = CreateTicket();
        canceled.Cancel("Solicitado pelo morador");
        canceled.Cancel("repetido");

        Assert.Equal(TicketStatusTypeEnum.Canceled, canceled.Status);
        Assert.Equal(2, canceled.Audits.Count);
        Assert.Contains("Solicitado pelo morador", canceled.Audits[^1].Action);
    }

    private static Ticket CreateTicket() => Ticket.Create(
        Guid.NewGuid(),
        "Boleto",
        DateTime.UtcNow.AddDays(5),
        Guid.NewGuid(),
        false,
        null,
        DateTime.UtcNow,
        DateTime.UtcNow);
}
