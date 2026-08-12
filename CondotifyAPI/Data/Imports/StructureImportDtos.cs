namespace CondotifyAPI.Data.Imports;

public sealed class StructureImportIn
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? PreviewId { get; set; }
    public string PreviewFileSha256 { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string ProcessingBasis { get; set; } = string.Empty;
    public string AuthorizedBy { get; set; } = string.Empty;
    public string AuthorizationReference { get; set; } = string.Empty;
    public bool ControllerAuthorizationConfirmed { get; set; }
    public bool PurposeLimitationConfirmed { get; set; }
    public bool NoRestrictedDataConfirmed { get; set; }
}

public sealed class StructureImportPreviewOut
{
    public Guid PreviewId { get; set; }
    public string FileSha256 { get; set; } = string.Empty;
    public bool CanExecute { get; set; }
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int NewBlocks { get; set; }
    public int NewUnits { get; set; }
    public int NewPeople { get; set; }
    public int NewVehicles { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<StructureImportRowOut> Rows { get; set; } = [];
}

public sealed class StructureImportRowOut
{
    public int RowNumber { get; set; }
    public string Block { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public List<string> Messages { get; set; } = [];
}

public sealed class StructureImportExecutionOut
{
    public Guid ImportId { get; set; }
    public int CreatedBlocks { get; set; }
    public int CreatedUnits { get; set; }
    public int CreatedPeople { get; set; }
    public int CreatedVehicles { get; set; }
    public string Message { get; set; } = string.Empty;
}
