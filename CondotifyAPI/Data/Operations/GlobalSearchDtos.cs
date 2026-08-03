namespace CondotifyAPI.Data.Operations;

public sealed class GlobalResidentSearchOut
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid UnitId { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public string CPF { get; set; } = string.Empty;
    public string RG { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AccessType { get; set; } = string.Empty;
    public bool Temporary { get; set; }
    public DateTime Expire { get; set; }
    public List<GlobalCredentialSearchOut> Credentials { get; set; } = [];
}

public sealed class GlobalCredentialSearchOut
{
    public string Type { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
