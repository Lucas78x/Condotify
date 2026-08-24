[Flags]
public enum LicensePermissionEnum : long
{
    None = 0,
    ViewDashboard = 1L << 0,
    ViewStructure = 1L << 1,
    ManageStructure = 1L << 2,
    ViewPeople = 1L << 3,
    ManagePeople = 1L << 4,
    ViewCredentials = 1L << 5,
    ManageCredentials = 1L << 6,
    ViewDevices = 1L << 7,
    ManageDevices = 1L << 8,
    OperateDevices = 1L << 9,
    ViewEvents = 1L << 10,
    ViewDeliveries = 1L << 11,
    ManageDeliveries = 1L << 12,
    ViewUsers = 1L << 13,
    ManageUsers = 1L << 14,
    ViewSettings = 1L << 15,
    ManageSettings = 1L << 16,
    ViewBookings = 1L << 17,
    ManageBookings = 1L << 18,
    ViewBackups = 1L << 19,
    ManageBackups = 1L << 20,
    ViewAlerts = 1L << 21,
    ManageAlerts = 1L << 22,
    ViewIncidents = 1L << 23,
    ManageIncidents = 1L << 24,
    ViewAutomations = 1L << 25,
    ManageAutomations = 1L << 26,
    ViewEmergency = 1L << 27,
    ManageEmergency = 1L << 28,
    ViewVehicles = 1L << 29,
    ManageVehicles = 1L << 30,
    ViewFinance = 1L << 31,
    ManageFinance = 1L << 32,
    ViewDocuments = 1L << 33,
    ManageDocuments = 1L << 34,
    ManageAnnouncements = 1L << 35,
    ViewAssemblies = 1L << 36,
    ManageAssemblies = 1L << 37,
    All = (1L << 38) - 1
}

public enum LicenseAccessRoleEnum
{
    Administrator = 0,
    Manager = 1,
    Concierge = 2,
    Operator = 3,
    Viewer = 4
}

public static class LicenseAccessDefaults
{
    public static LicensePermissionEnum ForRole(LicenseAccessRoleEnum role) => role switch
    {
        LicenseAccessRoleEnum.Administrator => LicensePermissionEnum.All,
        LicenseAccessRoleEnum.Manager => LicensePermissionEnum.All & ~LicensePermissionEnum.ManageUsers,
        LicenseAccessRoleEnum.Concierge => LicensePermissionEnum.ViewDashboard |
            LicensePermissionEnum.ViewStructure | LicensePermissionEnum.ViewPeople |
            LicensePermissionEnum.ManagePeople | LicensePermissionEnum.ViewCredentials |
            LicensePermissionEnum.ManageCredentials | LicensePermissionEnum.ViewDevices |
            LicensePermissionEnum.OperateDevices | LicensePermissionEnum.ViewEvents |
            LicensePermissionEnum.ViewDeliveries | LicensePermissionEnum.ManageDeliveries |
            LicensePermissionEnum.ViewBookings | LicensePermissionEnum.ManageBookings |
            LicensePermissionEnum.ViewAlerts | LicensePermissionEnum.ManageAlerts |
            LicensePermissionEnum.ViewIncidents | LicensePermissionEnum.ManageIncidents |
            LicensePermissionEnum.ViewAutomations | LicensePermissionEnum.ViewEmergency |
            LicensePermissionEnum.ManageEmergency,
        LicenseAccessRoleEnum.Operator => LicensePermissionEnum.ViewDashboard |
            LicensePermissionEnum.ViewStructure | LicensePermissionEnum.ViewPeople |
            LicensePermissionEnum.ViewCredentials | LicensePermissionEnum.ViewDevices |
            LicensePermissionEnum.OperateDevices | LicensePermissionEnum.ViewEvents |
            LicensePermissionEnum.ViewDeliveries | LicensePermissionEnum.ViewBookings |
            LicensePermissionEnum.ViewAlerts | LicensePermissionEnum.ViewIncidents |
            LicensePermissionEnum.ManageIncidents | LicensePermissionEnum.ViewAutomations |
            LicensePermissionEnum.ViewEmergency,
        _ => LicensePermissionEnum.ViewDashboard | LicensePermissionEnum.ViewStructure |
            LicensePermissionEnum.ViewPeople | LicensePermissionEnum.ViewCredentials |
            LicensePermissionEnum.ViewDevices | LicensePermissionEnum.ViewEvents |
            LicensePermissionEnum.ViewDeliveries | LicensePermissionEnum.ViewBookings |
            LicensePermissionEnum.ViewAlerts | LicensePermissionEnum.ViewIncidents |
            LicensePermissionEnum.ViewAutomations | LicensePermissionEnum.ViewEmergency
    };

    public static LicensePermissionEnum Normalize(LicensePermissionEnum permissions)
    {
        if (permissions.HasFlag(LicensePermissionEnum.ManageStructure)) permissions |= LicensePermissionEnum.ViewStructure;
        if (permissions.HasFlag(LicensePermissionEnum.ManagePeople)) permissions |= LicensePermissionEnum.ViewPeople | LicensePermissionEnum.ViewStructure;
        if (permissions.HasFlag(LicensePermissionEnum.ManageCredentials)) permissions |= LicensePermissionEnum.ViewCredentials | LicensePermissionEnum.ViewStructure | LicensePermissionEnum.ViewDevices;
        if (permissions.HasFlag(LicensePermissionEnum.ManageDevices)) permissions |= LicensePermissionEnum.ViewDevices;
        if (permissions.HasFlag(LicensePermissionEnum.OperateDevices)) permissions |= LicensePermissionEnum.ViewDevices;
        if (permissions.HasFlag(LicensePermissionEnum.ManageDeliveries)) permissions |= LicensePermissionEnum.ViewDeliveries;
        if (permissions.HasFlag(LicensePermissionEnum.ManageUsers)) permissions |= LicensePermissionEnum.ViewUsers;
        if (permissions.HasFlag(LicensePermissionEnum.ManageSettings)) permissions |= LicensePermissionEnum.ViewSettings;
        if (permissions.HasFlag(LicensePermissionEnum.ManageBookings)) permissions |= LicensePermissionEnum.ViewBookings;
        if (permissions.HasFlag(LicensePermissionEnum.ManageBackups)) permissions |= LicensePermissionEnum.ViewBackups;
        if (permissions.HasFlag(LicensePermissionEnum.ManageAlerts)) permissions |= LicensePermissionEnum.ViewAlerts;
        if (permissions.HasFlag(LicensePermissionEnum.ManageIncidents)) permissions |= LicensePermissionEnum.ViewIncidents;
        if (permissions.HasFlag(LicensePermissionEnum.ManageAutomations)) permissions |= LicensePermissionEnum.ViewAutomations;
        if (permissions.HasFlag(LicensePermissionEnum.ManageEmergency)) permissions |= LicensePermissionEnum.ViewEmergency | LicensePermissionEnum.ViewIncidents;
        if (permissions.HasFlag(LicensePermissionEnum.ManageFinance)) permissions |= LicensePermissionEnum.ViewFinance;
        if (permissions.HasFlag(LicensePermissionEnum.ManageDocuments)) permissions |= LicensePermissionEnum.ViewDocuments;
        if (permissions.HasFlag(LicensePermissionEnum.ManageAssemblies)) permissions |= LicensePermissionEnum.ViewAssemblies;
        return permissions;
    }
}
