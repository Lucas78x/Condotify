using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CondotifyAPI.Domain.DTO.Finance;

namespace CondotifyAPI.Services.Finance;

// CPF e texto de unidade sao criterios de reforco, nunca escolha por
// adivinhacao: qualquer ambiguidade (mais de uma unidade candidata) cai
// direto para o proximo criterio, e se nenhum criterio resolver sozinho a
// pagina fica Unmatched para revisao manual do sindico.
public static class BoletoPageMatcher
{
    public sealed record ResidentCandidate(Guid ResidentId, string Cpf, Guid UnitId);
    public sealed record UnitCandidate(Guid UnitId, string BlockName, string UnitNumber);
    public readonly record struct MatchResult(Guid? UnitId, BoletoMatchMethodEnum Method);

    private static readonly Regex CpfPattern = new(@"\d{3}\.?\d{3}\.?\d{3}-?\d{2}", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public static MatchResult Match(
        string pageText,
        IReadOnlyCollection<ResidentCandidate> residents,
        IReadOnlyCollection<UnitCandidate> units)
    {
        var byCpf = MatchByCpf(pageText, residents);
        if (byCpf.UnitId is not null) return byCpf;

        return MatchByUnitText(pageText, units);
    }

    internal static MatchResult MatchByCpf(string pageText, IReadOnlyCollection<ResidentCandidate> residents)
    {
        if (string.IsNullOrWhiteSpace(pageText) || residents.Count == 0)
            return new MatchResult(null, BoletoMatchMethodEnum.Unmatched);

        var cpfsFound = ExtractCpfs(pageText).ToHashSet();
        if (cpfsFound.Count == 0)
            return new MatchResult(null, BoletoMatchMethodEnum.Unmatched);

        // O CPF do candidato pode chegar formatado ("123.456.789-01"): a coluna
        // Resident.CPF aceita 14 caracteres e quase todos os fluxos de cadastro
        // gravam com pontuacao. Normaliza aqui tambem para o matcher nao depender
        // de o chamador ter lembrado de normalizar (defesa em profundidade).
        var matchedUnits = residents
            .Where(x => cpfsFound.Contains(DigitsOnly(x.Cpf)))
            .Select(x => x.UnitId)
            .Distinct()
            .ToList();

        return matchedUnits.Count == 1
            ? new MatchResult(matchedUnits[0], BoletoMatchMethodEnum.Cpf)
            : new MatchResult(null, BoletoMatchMethodEnum.Unmatched);
    }

    internal static MatchResult MatchByUnitText(string pageText, IReadOnlyCollection<UnitCandidate> units)
    {
        if (string.IsNullOrWhiteSpace(pageText) || units.Count == 0)
            return new MatchResult(null, BoletoMatchMethodEnum.Unmatched);

        var normalized = Normalize(pageText);
        var numberMatches = units
            .Where(unit => HasUnitNumberReference(normalized, unit.UnitNumber))
            .ToList();
        if (numberMatches.Count == 0)
            return new MatchResult(null, BoletoMatchMethodEnum.Unmatched);

        // O numero sozinho ja basta quando so existe uma unidade candidata com
        // esse numero (a maioria dos boletos nao repete "Bloco X"). So exige o
        // nome do bloco no texto para desempatar quando o numero por si so e
        // ambiguo entre unidades de blocos diferentes.
        var byNumber = numberMatches.Select(unit => unit.UnitId).Distinct().ToList();
        if (byNumber.Count == 1)
            return new MatchResult(byNumber[0], BoletoMatchMethodEnum.UnitText);

        var byNumberAndBlock = numberMatches
            .Where(unit => !string.IsNullOrWhiteSpace(Normalize(unit.BlockName)) &&
                           normalized.Contains(Normalize(unit.BlockName), StringComparison.Ordinal))
            .Select(unit => unit.UnitId)
            .Distinct()
            .ToList();

        return byNumberAndBlock.Count == 1
            ? new MatchResult(byNumberAndBlock[0], BoletoMatchMethodEnum.UnitText)
            : new MatchResult(null, BoletoMatchMethodEnum.Unmatched);
    }

    internal static IEnumerable<string> ExtractCpfs(string text) =>
        CpfPattern.Matches(text)
            .Select(match => DigitsOnly(match.Value))
            .Where(digits => digits.Length == 11);

    internal static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    internal static bool HasUnitNumberReference(string normalizedText, string unitNumber)
    {
        var unitKey = Normalize(unitNumber);
        if (string.IsNullOrWhiteSpace(unitKey)) return false;

        var unitPattern = $@"\b(apto|apartamento|unidade|ap)\.?\s*{Regex.Escape(unitKey)}\b";
        return Regex.IsMatch(normalizedText, unitPattern);
    }

    internal static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var stripped = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return WhitespacePattern.Replace(stripped, " ").Trim().ToLowerInvariant();
    }
}
