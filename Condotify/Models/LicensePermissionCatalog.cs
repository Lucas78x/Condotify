namespace Condotify.Models;

public sealed record LicensePermissionOption(LicensePermission Permission, string Group, string Name, string Description);

public static class LicensePermissionCatalog
{
    public static IReadOnlyList<LicensePermissionOption> Options { get; } =
    [
        new(LicensePermission.ViewDashboard, "Operacao", "Ver painel", "Indicadores e resumo do condominio"),
        new(LicensePermission.ViewEvents, "Operacao", "Consultar acessos", "Eventos autorizados, recusas e logs"),
        new(LicensePermission.OperateDevices, "Operacao", "Acionar equipamentos", "Abertura remota e comandos operacionais"),
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
        new(LicensePermission.ViewUsers, "Administracao", "Ver usuarios", "Equipe vinculada ao condominio"),
        new(LicensePermission.ManageUsers, "Administracao", "Gerenciar usuarios", "Criar acessos e alterar permissoes"),
        new(LicensePermission.ViewSettings, "Administracao", "Ver configuracoes", "Politicas e parametros da licenca"),
        new(LicensePermission.ManageSettings, "Administracao", "Editar configuracoes", "Alterar politicas de credenciais")
    ];

    public static long Defaults(int role) => role switch
    {
        0 => (long)LicensePermission.All,
        1 => (long)(LicensePermission.All & ~LicensePermission.ManageUsers),
        2 => (long)(LicensePermission.ViewDashboard | LicensePermission.ViewStructure | LicensePermission.ViewPeople |
            LicensePermission.ManagePeople | LicensePermission.ViewCredentials | LicensePermission.ManageCredentials |
            LicensePermission.ViewDevices | LicensePermission.OperateDevices | LicensePermission.ViewEvents |
            LicensePermission.ViewDeliveries | LicensePermission.ManageDeliveries),
        3 => (long)(LicensePermission.ViewDashboard | LicensePermission.ViewStructure | LicensePermission.ViewPeople |
            LicensePermission.ViewCredentials | LicensePermission.ViewDevices | LicensePermission.OperateDevices |
            LicensePermission.ViewEvents | LicensePermission.ViewDeliveries),
        _ => (long)(LicensePermission.ViewDashboard | LicensePermission.ViewStructure | LicensePermission.ViewPeople |
            LicensePermission.ViewCredentials | LicensePermission.ViewDevices | LicensePermission.ViewEvents |
            LicensePermission.ViewDeliveries)
    };

    public static long Normalize(long permissions)
    {
        var value = (LicensePermission)permissions;
        if (value.HasFlag(LicensePermission.ManageStructure)) value |= LicensePermission.ViewStructure;
        if (value.HasFlag(LicensePermission.ManagePeople)) value |= LicensePermission.ViewPeople | LicensePermission.ViewStructure;
        if (value.HasFlag(LicensePermission.ManageCredentials)) value |= LicensePermission.ViewCredentials | LicensePermission.ViewStructure | LicensePermission.ViewDevices;
        if (value.HasFlag(LicensePermission.ManageDevices) || value.HasFlag(LicensePermission.OperateDevices)) value |= LicensePermission.ViewDevices;
        if (value.HasFlag(LicensePermission.ManageDeliveries)) value |= LicensePermission.ViewDeliveries;
        if (value.HasFlag(LicensePermission.ManageUsers)) value |= LicensePermission.ViewUsers;
        if (value.HasFlag(LicensePermission.ManageSettings)) value |= LicensePermission.ViewSettings;
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
            LicensePermission.ViewUsers => ~LicensePermission.ManageUsers,
            LicensePermission.ViewSettings => ~LicensePermission.ManageSettings,
            _ => LicensePermission.All
        };
        return (long)value;
    }
}
