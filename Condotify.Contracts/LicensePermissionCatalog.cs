namespace Condotify.Models;

public sealed record LicensePermissionOption(LicensePermission Permission, string Group, string Name, string Description);

public static class LicensePermissionCatalog
{
    public static IReadOnlyList<LicensePermissionOption> Options { get; } =
    [
        new(LicensePermission.ViewDashboard, "Operacao", "Ver painel", "Indicadores e resumo do condominio"),
        new(LicensePermission.ViewEvents, "Operacao", "Consultar acessos", "Eventos autorizados, recusas e logs"),
        new(LicensePermission.OperateDevices, "Operacao", "Acionar equipamentos", "Abertura remota e comandos operacionais"),
        new(LicensePermission.ViewAlerts, "Operacao", "Ver alertas", "Falhas, avisos e saude operacional"),
        new(LicensePermission.ManageAlerts, "Operacao", "Tratar alertas", "Reconhecer, resolver e reabrir ocorrencias"),
        new(LicensePermission.ViewIncidents, "Operacao", "Ver ocorrencias", "Central, historico e linha do tempo operacional"),
        new(LicensePermission.ManageIncidents, "Operacao", "Tratar ocorrencias", "Abrir, atribuir, comentar e resolver ocorrencias"),
        new(LicensePermission.ViewAutomations, "Operacao", "Ver automacoes", "Consultar regras e execucoes automaticas"),
        new(LicensePermission.ManageAutomations, "Operacao", "Gerenciar automacoes", "Criar, editar, ativar e testar regras"),
        new(LicensePermission.ViewEmergency, "Seguranca", "Ver modo de emergencia", "Acompanhar emergencias e orientacoes ativas"),
        new(LicensePermission.ManageEmergency, "Seguranca", "Ativar modo de emergencia", "Ativar e encerrar protocolos com auditoria"),
        new(LicensePermission.ViewStructure, "Cadastros", "Ver estrutura", "Blocos, unidades e pessoas"),
        new(LicensePermission.ManageStructure, "Cadastros", "Editar estrutura", "Criar e alterar blocos e unidades"),
        new(LicensePermission.ViewPeople, "Cadastros", "Ver pessoas", "Moradores, visitantes e prestadores"),
        new(LicensePermission.ManagePeople, "Cadastros", "Editar pessoas", "Cadastros, veiculos e convites"),
        new(LicensePermission.ViewCredentials, "Credenciais", "Ver credenciais", "Faciais, QR Codes, cartoes e tags"),
        new(LicensePermission.ManageCredentials, "Credenciais", "Gerenciar credenciais", "Emitir, ativar, renovar e restaurar"),
        new(LicensePermission.ViewDevices, "Infraestrutura", "Ver equipamentos", "Terminais e cameras"),
        new(LicensePermission.ManageDevices, "Infraestrutura", "Configurar equipamentos", "Rede, modelos e conexoes"),
        new(LicensePermission.ViewDeliveries, "Portaria", "Ver encomendas", "Consulta da operacao de encomendas"),
        new(LicensePermission.ManageDeliveries, "Portaria", "Gerenciar encomendas", "Receber e entregar encomendas"),
        new(LicensePermission.ViewBookings, "Areas comuns", "Ver agendamentos", "Consulta de locais e reservas de areas comuns"),
        new(LicensePermission.ManageBookings, "Areas comuns", "Gerenciar agendamentos", "Cadastrar locais e aprovar, recusar ou cancelar reservas"),
        new(LicensePermission.ViewUsers, "Administracao", "Ver usuarios", "Equipe vinculada ao condominio"),
        new(LicensePermission.ManageUsers, "Administracao", "Gerenciar usuarios", "Criar acessos e alterar permissoes"),
        new(LicensePermission.ViewSettings, "Administracao", "Ver configuracoes", "Politicas e parametros da licenca"),
        new(LicensePermission.ManageSettings, "Administracao", "Editar configuracoes", "Alterar politicas de credenciais"),
        new(LicensePermission.ViewBackups, "Seguranca", "Ver backups", "Historico de configuracoes e simulacoes"),
        new(LicensePermission.ManageBackups, "Seguranca", "Gerenciar backups", "Criar, restaurar e excluir snapshots")
    ];

    public static long Defaults(int role) => role switch
    {
        0 => (long)LicensePermission.All,
        1 => (long)(LicensePermission.All & ~LicensePermission.ManageUsers),
        2 => (long)(LicensePermission.ViewDashboard | LicensePermission.ViewStructure | LicensePermission.ViewPeople |
            LicensePermission.ManagePeople | LicensePermission.ViewCredentials | LicensePermission.ManageCredentials |
            LicensePermission.ViewDevices | LicensePermission.OperateDevices | LicensePermission.ViewEvents |
            LicensePermission.ViewDeliveries | LicensePermission.ManageDeliveries |
            LicensePermission.ViewBookings | LicensePermission.ManageBookings |
            LicensePermission.ViewAlerts | LicensePermission.ManageAlerts |
            LicensePermission.ViewIncidents | LicensePermission.ManageIncidents |
            LicensePermission.ViewAutomations | LicensePermission.ViewEmergency |
            LicensePermission.ManageEmergency),
        3 => (long)(LicensePermission.ViewDashboard | LicensePermission.ViewStructure | LicensePermission.ViewPeople |
            LicensePermission.ViewCredentials | LicensePermission.ViewDevices | LicensePermission.OperateDevices |
            LicensePermission.ViewEvents | LicensePermission.ViewDeliveries | LicensePermission.ViewBookings |
            LicensePermission.ViewAlerts | LicensePermission.ViewIncidents |
            LicensePermission.ManageIncidents | LicensePermission.ViewAutomations |
            LicensePermission.ViewEmergency),
        _ => (long)(LicensePermission.ViewDashboard | LicensePermission.ViewStructure | LicensePermission.ViewPeople |
            LicensePermission.ViewCredentials | LicensePermission.ViewDevices | LicensePermission.ViewEvents |
            LicensePermission.ViewDeliveries | LicensePermission.ViewBookings |
            LicensePermission.ViewAlerts | LicensePermission.ViewIncidents |
            LicensePermission.ViewAutomations | LicensePermission.ViewEmergency)
    };

    public static long Normalize(long permissions)
    {
        var value = (LicensePermission)permissions;
        if (value.HasFlag(LicensePermission.ManageStructure)) value |= LicensePermission.ViewStructure;
        if (value.HasFlag(LicensePermission.ManagePeople)) value |= LicensePermission.ViewPeople | LicensePermission.ViewStructure;
        if (value.HasFlag(LicensePermission.ManageCredentials)) value |= LicensePermission.ViewCredentials | LicensePermission.ViewStructure | LicensePermission.ViewDevices;
        if (value.HasFlag(LicensePermission.ManageDevices) || value.HasFlag(LicensePermission.OperateDevices)) value |= LicensePermission.ViewDevices;
        if (value.HasFlag(LicensePermission.ManageDeliveries)) value |= LicensePermission.ViewDeliveries;
        if (value.HasFlag(LicensePermission.ManageBookings)) value |= LicensePermission.ViewBookings;
        if (value.HasFlag(LicensePermission.ManageUsers)) value |= LicensePermission.ViewUsers;
        if (value.HasFlag(LicensePermission.ManageSettings)) value |= LicensePermission.ViewSettings;
        if (value.HasFlag(LicensePermission.ManageBackups)) value |= LicensePermission.ViewBackups;
        if (value.HasFlag(LicensePermission.ManageAlerts)) value |= LicensePermission.ViewAlerts;
        if (value.HasFlag(LicensePermission.ManageIncidents)) value |= LicensePermission.ViewIncidents;
        if (value.HasFlag(LicensePermission.ManageAutomations)) value |= LicensePermission.ViewAutomations;
        if (value.HasFlag(LicensePermission.ManageEmergency)) value |= LicensePermission.ViewEmergency | LicensePermission.ViewIncidents;
        return (long)value;
    }

    public static long RemoveDependents(long permissions, LicensePermission permission)
    {
        var value = (LicensePermission)permissions;
        value &= ~permission;
        value &= permission switch
        {
            LicensePermission.ViewStructure => ~(LicensePermission.ManageStructure | LicensePermission.ManagePeople | LicensePermission.ManageCredentials),
            LicensePermission.ViewPeople => ~LicensePermission.ManagePeople,
            LicensePermission.ViewCredentials => ~LicensePermission.ManageCredentials,
            LicensePermission.ViewDevices => ~(LicensePermission.ManageDevices | LicensePermission.OperateDevices | LicensePermission.ManageCredentials),
            LicensePermission.ViewDeliveries => ~LicensePermission.ManageDeliveries,
            LicensePermission.ViewBookings => ~LicensePermission.ManageBookings,
            LicensePermission.ViewUsers => ~LicensePermission.ManageUsers,
            LicensePermission.ViewSettings => ~LicensePermission.ManageSettings,
            LicensePermission.ViewBackups => ~LicensePermission.ManageBackups,
            LicensePermission.ViewAlerts => ~LicensePermission.ManageAlerts,
            LicensePermission.ViewIncidents => ~(LicensePermission.ManageIncidents | LicensePermission.ManageEmergency),
            LicensePermission.ViewAutomations => ~LicensePermission.ManageAutomations,
            LicensePermission.ViewEmergency => ~LicensePermission.ManageEmergency,
            _ => LicensePermission.All
        };
        return (long)value;
    }
}
