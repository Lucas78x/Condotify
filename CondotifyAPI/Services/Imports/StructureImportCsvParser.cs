using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace CondotifyAPI.Services.Imports;

public sealed class StructureImportCsvParser
{
    private const int MaxRows = 1_000;

    private static readonly IReadOnlyDictionary<string, string> HeaderAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["agrupamento"] = "block", ["bloco"] = "block", ["quadra"] = "block", ["setor"] = "block", ["group"] = "block",
            ["unidade"] = "unit", ["sala"] = "unit", ["lote"] = "unit", ["apartamento"] = "unit", ["casa"] = "unit", ["unit"] = "unit",
            ["andar"] = "floor", ["pavimento"] = "floor",
            ["nome"] = "name", ["pessoa"] = "name", ["morador"] = "name",
            ["categoria"] = "category", ["perfil"] = "category", ["tipopessoa"] = "category",
            ["vinculo"] = "relationship", ["relacao"] = "relationship", ["relacionamento"] = "relationship",
            ["cpf"] = "cpf", ["rg"] = "rg", ["email"] = "email", ["telefone"] = "phone", ["celular"] = "phone",
            ["placa"] = "plate", ["marcaveiculo"] = "vehicleBrand", ["modeloveiculo"] = "vehicleModel",
            ["corveiculo"] = "vehicleColor", ["tipoveiculo"] = "vehicleType",
            ["tag"] = "tag", ["tagveicular"] = "tag", ["uhf"] = "tag"
        };

    private static readonly HashSet<string> RestrictedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "senha", "password", "passwordhash", "hashdesenha", "pin", "codigoacesso", "accesscode",
        "biometria", "biometric", "templatebiometrico", "biometrictemplate", "digital", "impressaodigital",
        "fingerprint", "face", "facial", "templatefacial", "facetemplate", "fotofacial", "imagemfacial"
    };

    public StructureImportParseResult Parse(string? content)
    {
        var result = new StructureImportParseResult();
        if (string.IsNullOrWhiteSpace(content))
        {
            result.Errors.Add("O arquivo esta vazio.");
            return result;
        }
        if (content.Length > 2_000_000)
        {
            result.Errors.Add("O arquivo excede o limite de 2 MB.");
            return result;
        }

        var delimiter = DetectDelimiter(content);
        using var reader = new StringReader(content.TrimStart('\uFEFF'));
        using var parser = new TextFieldParser(reader)
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true,
            TextFieldType = FieldType.Delimited
        };
        parser.SetDelimiters(delimiter.ToString());

        string[]? headers;
        try
        {
            headers = parser.ReadFields();
        }
        catch (MalformedLineException exception)
        {
            result.Errors.Add($"Cabecalho CSV invalido: {exception.Message}");
            return result;
        }

        if (headers is null)
        {
            result.Errors.Add("O arquivo nao possui cabecalho.");
            return result;
        }

        var headerMap = BuildHeaderMap(headers, result.Errors);
        if (!headerMap.ContainsKey("block")) result.Errors.Add("Adicione a coluna Agrupamento, Bloco ou Quadra.");
        if (!headerMap.ContainsKey("unit")) result.Errors.Add("Adicione a coluna Unidade, Sala, Lote ou Apartamento.");
        if (result.Errors.Count > 0) return result;

        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            if (result.Rows.Count >= MaxRows)
            {
                result.Errors.Add($"O arquivo possui mais de {MaxRows} registros.");
                break;
            }

            try
            {
                var fields = parser.ReadFields() ?? [];
                if (fields.All(string.IsNullOrWhiteSpace)) continue;
                result.Rows.Add(ToRow(rowNumber, fields, headerMap));
            }
            catch (MalformedLineException exception)
            {
                result.Rows.Add(new StructureImportParsedRow
                {
                    RowNumber = rowNumber,
                    Errors = [$"Linha CSV invalida: {exception.Message}"]
                });
            }
        }

        if (result.Rows.Count == 0 && result.Errors.Count == 0)
            result.Errors.Add("O arquivo nao possui registros para importar.");
        return result;
    }

    public static string NormalizeKey(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private static Dictionary<string, int> BuildHeaderMap(string[] headers, ICollection<string> errors)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Length; index++)
        {
            var normalized = NormalizeKey(headers[index]);
            if (RestrictedHeaders.Contains(normalized))
            {
                errors.Add($"A coluna {headers[index]} contém dado restrito. Remova senhas, PINs e dados biométricos antes de enviar o arquivo.");
                continue;
            }
            if (!HeaderAliases.TryGetValue(normalized, out var canonical)) continue;
            if (!map.TryAdd(canonical, index))
                errors.Add($"A coluna {headers[index]} aparece mais de uma vez.");
        }
        return map;
    }

    private static StructureImportParsedRow ToRow(int rowNumber, string[] fields, IReadOnlyDictionary<string, int> map)
    {
        string Read(string name) => map.TryGetValue(name, out var index) && index < fields.Length ? fields[index].Trim() : string.Empty;
        return new StructureImportParsedRow
        {
            RowNumber = rowNumber,
            Block = Read("block"),
            Unit = Read("unit"),
            Floor = Read("floor"),
            Name = Read("name"),
            Category = Read("category"),
            Relationship = Read("relationship"),
            CPF = Digits(Read("cpf")),
            RG = Read("rg"),
            Email = Read("email"),
            Phone = Read("phone"),
            Plate = NormalizePlate(Read("plate")),
            VehicleBrand = Read("vehicleBrand"),
            VehicleModel = Read("vehicleModel"),
            VehicleColor = Read("vehicleColor"),
            VehicleType = Read("vehicleType"),
            Tag = Read("tag")
        };
    }

    private static char DetectDelimiter(string content)
    {
        var firstLine = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var semicolons = CountOutsideQuotes(firstLine, ';');
        var commas = CountOutsideQuotes(firstLine, ',');
        return semicolons >= commas ? ';' : ',';
    }

    private static int CountOutsideQuotes(string value, char delimiter)
    {
        var count = 0;
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '"')
            {
                if (quoted && index + 1 < value.Length && value[index + 1] == '"') index++;
                else quoted = !quoted;
            }
            else if (!quoted && value[index] == delimiter) count++;
        }
        return count;
    }

    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string NormalizePlate(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

public sealed class StructureImportParseResult
{
    public List<string> Errors { get; set; } = [];
    public List<StructureImportParsedRow> Rows { get; set; } = [];
}

public sealed class StructureImportParsedRow
{
    public int RowNumber { get; set; }
    public string Block { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string CPF { get; set; } = string.Empty;
    public string RG { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Plate { get; set; } = string.Empty;
    public string VehicleBrand { get; set; } = string.Empty;
    public string VehicleModel { get; set; } = string.Empty;
    public string VehicleColor { get; set; } = string.Empty;
    public string VehicleType { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = [];
}
