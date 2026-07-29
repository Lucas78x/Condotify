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
    All = (1L << 21) - 1
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
            LicensePermissionEnum.ViewBookings | LicensePermissionEnum.ManageBookings,
        LicenseAccessRoleEnum.Operator => LicensePermissionEnum.ViewDashboard |
            LicensePermissionEnum.ViewStructure | LicensePermissionEnum.ViewPeople |
            LicensePermissionEnum.ViewCredentials | LicensePermissionEnum.ViewDevices |
            LicensePermissionEnum.OperateDevices | LicensePermissionEnum.ViewEvents |
            LicensePermissionEnum.ViewDeliveries | LicensePermissionEnum.ViewBookings,
        _ => LicensePermissionEnum.ViewDashboard | LicensePermissionEnum.ViewStructure |
            LicensePermissionEnum.ViewPeople | LicensePermissionEnum.ViewCredentials |
            LicensePermissionEnum.ViewDevices | LicensePermissionEnum.ViewEvents |
            LicensePermissionEnum.ViewDeliveries | LicensePermissionEnum.ViewBookings
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
        return permissions;
    }
}
