namespace CondotifyAPI.Domain.Enums.License;

// Espelha Condotify.Models.LicenseModuleEnum (Condotify.Contracts) bit a bit.
// Mudar um sem mudar o outro quebra a leitura do bitmask no cliente.
[Flags]
public enum LicenseModuleEnum : long
{
    None = 0,
    Cameras = 1L << 0,
    Devices = 1L << 1,
    Routes = 1L << 2,
    Incidents = 1L << 3,
    Automations = 1L << 4,
    Emergency = 1L << 5,
    Deliveries = 1L << 6,
    Bookings = 1L << 7,
    Finance = 1L << 8,
    Documents = 1L << 9,
    Announcements = 1L << 10,
    Assemblies = 1L << 11,
    All = (1L << 12) - 1
}
