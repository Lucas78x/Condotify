using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Condotify.Models;

namespace CondotifyAPI.Services.Finance;

public sealed record FinancialImportUnit(Guid Id, string Block, string Unit, string Label);

public sealed class FinancialChargeImportCsvParser
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Regex CompetencePattern = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private static readonly string[] RequiredHeaders =
    [
        "bloco", "unidade", "competencia", "referencia", "descricao", "vencimento", "valor"
    ];

    public FinancialImportPreviewViewModel Parse(
        string fileName,
        string content,
        IReadOnlyCollection<FinancialImportUnit> units,
        IReadOnlySet<string>? existingKeys = null)
    {
        var result = new FinancialImportPreviewViewModel { FileName = Path.GetFileName(fileName ?? string.Empty) };
        if (string.IsNullOrWhiteSpace(content))
        {
            result.Errors.Add("O arquivo está vazio.");
            return result;
        }

        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (lines.Count < 2)
        {
            result.Errors.Add("Inclua o cabeçalho e pelo menos uma cobrança.");
            return result;
        }
        if (lines.Count - 1 > 1000)
        {
            result.Errors.Add("A planilha pode conter no máximo 1.000 cobranças.");
            return result;
        }

        var delimiter = CountOutsideQuotes(lines[0], ';') >= CountOutsideQuotes(lines[0], ',') ? ';' : ',';
        var headers = ParseLine(lines[0], delimiter).Select(Normalize).ToList();
        var indexes = headers.Select((value, index) => (value, index))
            .Where(x => !string.IsNullOrWhiteSpace(x.value))
            .GroupBy(x => x.value)
            .ToDictionary(x => x.Key, x => x.First().index, StringComparer.Ordinal);
        var missing = RequiredHeaders.Where(x => !indexes.ContainsKey(x)).ToList();
        if (missing.Count > 0)
        {
            result.Errors.Add($"Colunas obrigatórias ausentes: {string.Join(", ", missing)}.");
            return result;
        }

        var unitLookup = units.GroupBy(x => UnitKey(x.Block, x.Unit))
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var fileKeys = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 1; index < lines.Count; index++)
        {
            var values = ParseLine(lines[index], delimiter);
            var row = new FinancialImportRowViewModel { RowNumber = index + 1 };
            string Read(string name) => indexes.TryGetValue(name, out var column) && column < values.Count ? values[column].Trim() : string.Empty;

            row.Block = Read("bloco");
            row.Unit = Read("unidade");
            row.Competence = Read("competencia");
            row.Reference = Read("referencia");
            row.Description = Read("descricao");
            row.Notes = Read("observacoes");

            if (!unitLookup.TryGetValue(UnitKey(row.Block, row.Unit), out var unit))
                row.Messages.Add("Bloco ou unidade não encontrado neste condomínio.");
            else
            {
                row.UnitId = unit.Id;
                row.UnitLabel = unit.Label;
            }

            if (!CompetencePattern.IsMatch(row.Competence))
                row.Messages.Add("Competência inválida; use AAAA-MM.");
            if (string.IsNullOrWhiteSpace(row.Reference) || row.Reference.Length > 80)
                row.Messages.Add("Referência obrigatória, com até 80 caracteres.");
            if (string.IsNullOrWhiteSpace(row.Description) || row.Description.Length > 200)
                row.Messages.Add("Descrição obrigatória, com até 200 caracteres.");
            if (row.Notes.Length > 1000)
                row.Messages.Add("Observações excedem 1.000 caracteres.");

            if (TryDate(Read("vencimento"), out var dueDate)) row.DueDate = dueDate;
            else row.Messages.Add("Vencimento inválido; use DD/MM/AAAA.");
            row.BaseAmount = ReadMoney(Read("valor"), "Valor", row.Messages, requiredPositive: true);
            row.FineAmount = ReadMoney(Read("multa"), "Multa", row.Messages);
            row.InterestAmount = ReadMoney(Read("juros"), "Juros", row.Messages);
            row.DiscountAmount = ReadMoney(Read("desconto"), "Desconto", row.Messages);
            row.TotalAmount = FinancialChargeCalculator.Total(row.BaseAmount, row.FineAmount, row.InterestAmount, row.DiscountAmount);
            if (row.TotalAmount <= 0) row.Messages.Add("O total gerencial deve ser maior que zero.");

            if (row.UnitId.HasValue && CompetencePattern.IsMatch(row.Competence) && !string.IsNullOrWhiteSpace(row.Reference))
            {
                var key = ChargeKey(row.UnitId.Value, row.Competence, row.Reference);
                if (!fileKeys.Add(key)) row.Messages.Add("Cobrança duplicada dentro da planilha.");
                if (existingKeys?.Contains(key) == true) row.Messages.Add("Já existe uma cobrança com esta unidade, competência e referência.");
            }
            result.Rows.Add(row);
        }

        result.TotalRows = result.Rows.Count;
        result.ValidRows = result.Rows.Count(x => x.IsValid);
        result.InvalidRows = result.TotalRows - result.ValidRows;
        result.TotalAmount = result.Rows.Where(x => x.IsValid).Sum(x => x.TotalAmount);
        return result;
    }

    public static string ChargeKey(Guid unitId, string competence, string reference) =>
        $"{unitId:N}|{competence.Trim()}|{Normalize(reference)}";

    private static decimal ReadMoney(string value, string label, ICollection<string> errors, bool requiredPositive = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (requiredPositive) errors.Add($"{label} deve ser maior que zero.");
            return 0m;
        }
        var styles = NumberStyles.Number | NumberStyles.AllowCurrencySymbol;
        if (!decimal.TryParse(value, styles, PtBr, out var amount) &&
            !decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out amount))
        {
            errors.Add($"{label} inválido.");
            return 0m;
        }
        amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount < 0 || requiredPositive && amount <= 0)
            errors.Add(requiredPositive ? $"{label} deve ser maior que zero." : $"{label} não pode ser negativo.");
        return amount;
    }

    private static bool TryDate(string value, out DateTime result)
    {
        var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(value.Trim(), formats, PtBr, DateTimeStyles.None, out var parsed))
        {
            result = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
            return true;
        }
        result = default;
        return false;
    }

    private static string UnitKey(string block, string unit) => $"{Normalize(block)}|{Normalize(unit)}";

    private static string Normalize(string value)
    {
        var decomposed = (value ?? string.Empty).Trim().TrimStart('\uFEFF').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        return builder.ToString().Normalize(NormalizationForm.FormC).Replace(" ", string.Empty).Replace("_", string.Empty);
    }

    private static int CountOutsideQuotes(string value, char character)
    {
        var count = 0;
        var quoted = false;
        foreach (var item in value)
        {
            if (item == '"') quoted = !quoted;
            else if (!quoted && item == character) count++;
        }
        return count;
    }

    private static List<string> ParseLine(string line, char delimiter)
    {
        var result = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (character == delimiter && !quoted)
            {
                result.Add(value.ToString());
                value.Clear();
            }
            else value.Append(character);
        }
        result.Add(value.ToString());
        return result;
    }
}
