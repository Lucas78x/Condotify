namespace Condotify.Models;

public sealed record LicensePermissionOption(LicensePermission Permission, string Group, string Name, string Description);

public static class LicensePermissionCatalog
{
    public static IReadOnlyList<LicensePermissionOption> Options { get; } =
    [
        new(LicensePermission.ViewDashboard, "Operação", "Ver painel", "Indicadores e resumo do condomínio"),
        new(LicensePermission.ViewEvents, "Operação", "Consultar acessos", "Eventos autorizados, recusas e registros"),
        new(LicensePermission.OperateDevices, "Operação", "Acionar equipamentos", "Abertura remota e comandos operacionais"),
        new(LicensePermission.ViewAlerts, "Operação", "Ver alertas", "Falhas, avisos e saúde operacional"),
        new(LicensePermission.ManageAlerts, "Operação", "Tratar alertas", "Reconhecer, resolver e reabrir ocorrências"),
        new(LicensePermission.ViewIncidents, "Operação", "Ver ocorrências", "Central, histórico e linha do tempo operacional"),
        new(LicensePermission.ManageIncidents, "Operação", "Tratar ocorrências", "Abrir, atribuir, comentar e resolver ocorrências"),
        new(LicensePermission.ViewAutomations, "Operação", "Ver automações", "Consultar regras e execuções automáticas"),
        new(LicensePermission.ManageAutomations, "Operação", "Gerenciar automações", "Criar, editar, ativar e testar regras"),
        new(LicensePermission.ViewEmergency, "Segurança", "Ver modo de emergência", "Acompanhar emergências e orientações ativas"),
        new(LicensePermission.ManageEmergency, "Segurança", "Ativar modo de emergência", "Ativar e encerrar protocolos com auditoria"),
        new(LicensePermission.ViewStructure, "Cadastros", "Ver estrutura", "Blocos, unidades e pessoas"),
        new(LicensePermission.ManageStructure, "Cadastros", "Editar estrutura", "Criar e alterar blocos e unidades"),
        new(LicensePermission.ViewPeople, "Cadastros", "Ver pessoas", "Moradores, visitantes e prestadores"),
        new(LicensePermission.ManagePeople, "Cadastros", "Editar pessoas", "Cadastros, veículos e convites"),
        new(LicensePermission.ViewCredentials, "Credenciais", "Ver credenciais", "Faciais, QR Codes, cartões e tags"),
        new(LicensePermission.ManageCredentials, "Credenciais", "Gerenciar credenciais", "Emitir, ativar, renovar e restaurar"),
        new(LicensePermission.ViewDevices, "Infraestrutura", "Ver equipamentos", "Terminais e câmeras"),
        new(LicensePermission.ManageDevices, "Infraestrutura", "Configurar equipamentos", "Rede, modelos e conexões"),
        new(LicensePermission.ViewDeliveries, "Portaria", "Ver encomendas", "Consulta da operação de encomendas"),
        new(LicensePermission.ManageDeliveries, "Portaria", "Gerenciar encomendas", "Receber e entregar encomendas"),
        new(LicensePermission.ViewBookings, "Áreas comuns", "Ver agendamentos", "Consulta de locais e reservas de áreas comuns"),
        new(LicensePermission.ManageBookings, "Áreas comuns", "Gerenciar agendamentos", "Cadastrar locais e aprovar, recusar ou cancelar reservas"),
        new(LicensePermission.ViewFinance, "Financeiro", "Ver financeiro", "Indicadores, cobranças, inadimplência e boletos"),
        new(LicensePermission.ManageFinance, "Financeiro", "Gerenciar financeiro", "Criar cobranças, importar, publicar e conciliar dados"),
        new(LicensePermission.ViewDocuments, "Documentos", "Ver documentos", "Consultar e baixar arquivos do condomínio"),
        new(LicensePermission.ManageDocuments, "Documentos", "Gerenciar documentos", "Enviar, organizar e remover arquivos"),
        new(LicensePermission.ManageAnnouncements, "Comunicação", "Gerenciar comunicados", "Criar, publicar e administrar comunicados"),
        new(LicensePermission.ViewUsers, "Administração", "Ver usuários", "Equipe vinculada ao condomínio"),
        new(LicensePermission.ManageUsers, "Administração", "Gerenciar usuários", "Criar acessos e alterar permissões"),
        new(LicensePermission.ViewSettings, "Administração", "Ver configurações", "Políticas e parâmetros da licença"),
        new(LicensePermission.ManageSettings, "Administração", "Editar configurações", "Alterar políticas de credenciais"),
        new(LicensePermission.ViewBackups, "Segurança", "Ver backups", "Histórico de configurações e simulações"),
        new(LicensePermission.ManageBackups, "Segurança", "Gerenciar backups", "Criar, restaurar e excluir versões")
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
        if (value.HasFlag(LicensePermission.ManageFinance)) value |= LicensePermission.ViewFinance;
        if (value.HasFlag(LicensePermission.ManageDocuments)) value |= LicensePermission.ViewDocuments;
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
            LicensePermission.ViewFinance => ~LicensePermission.ManageFinance,
            LicensePermission.ViewDocuments => ~LicensePermission.ManageDocuments,
            _ => LicensePermission.All
        };
        return (long)value;
    }
}
