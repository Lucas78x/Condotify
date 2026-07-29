
using System.ComponentModel;

public enum TicketStatusTypeEnum
{
    None = 0,
    [Description("Enviado")]
    Send = 1,
    [Description("Visualizado")]
    Viewed = 2,
    [Description("Expirado")]
    Expired = 3,
    [Description("Cancelado")]
    Canceled = 4,
}

