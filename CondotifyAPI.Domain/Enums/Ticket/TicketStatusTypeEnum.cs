
using System.ComponentModel;

public enum TicketStatusTypeEnum
{
    None = 0,
    [Description("Enviado")]
    Send = 1,
    [Description("Visualizado")]
    Viewed = 2,
}

