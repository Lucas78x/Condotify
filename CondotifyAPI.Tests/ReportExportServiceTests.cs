using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Condotify.Models;
using CondotifyAPI.Services.Reports;

namespace CondotifyAPI.Tests;

public sealed class ReportExportServiceTests
{
    [Fact]
    public void CreateExcel_GeneratesValidWorkbookWithAllReportSheets()
    {
        var bytes = ReportExportService.CreateExcel(CreateReport(), "Condomínio Horizonte", "HOR-001");
        WriteQaArtifact("relatorio-condotify-qa.xlsx", bytes);

        Assert.True(bytes.Length > 5_000);
        Assert.Equal("PK", Encoding.ASCII.GetString(bytes, 0, 2));

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var expectedEntries = new[]
        {
            "[Content_Types].xml",
            "xl/workbook.xml",
            "xl/styles.xml",
            "xl/worksheets/sheet1.xml",
            "xl/worksheets/sheet2.xml",
            "xl/worksheets/sheet3.xml",
            "xl/worksheets/sheet4.xml"
        };

        foreach (var expected in expectedEntries)
            Assert.NotNull(archive.GetEntry(expected));

        foreach (var entry in archive.Entries.Where(x => x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var entryStream = entry.Open();
            _ = XDocument.Load(entryStream);
        }

        var workbook = ReadEntry(archive, "xl/workbook.xml");
        Assert.Contains("Resumo executivo", workbook);
        Assert.Contains("Qualidade cadastral", workbook);
        Assert.Contains("Adoção por bloco", workbook);
        Assert.Contains("Operação", workbook);

        var summary = ReadEntry(archive, "xl/worksheets/sheet1.xml");
        Assert.Contains("Condomínio Horizonte", summary);
        Assert.Contains("ÍNDICE DE QUALIDADE", summary);
    }

    [Fact]
    public void CreatePdf_GeneratesThreePagePdfWithExecutiveSections()
    {
        var bytes = ReportExportService.CreatePdf(CreateReport(), "Condomínio Horizonte", "HOR-001");
        WriteQaArtifact("relatorio-condotify-qa.pdf", bytes);
        var pdf = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("/Count 3", pdf);
        Assert.Equal(3, CountOccurrences(pdf, "/Type /Page "));
        Assert.Contains("RELATÓRIO EXECUTIVO", pdf);
        Assert.Contains("Condomínio Horizonte", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
    }

    [Fact]
    public void CreateCsv_UsesUtf8BomAndPortugueseHeadings()
    {
        var bytes = ReportExportService.CreateCsv(CreateReport(), "Condomínio Horizonte", "HOR-001");

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var csv = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"Condomínio\";\"Condomínio Horizonte\"", csv);
        Assert.Contains("Índice de qualidade", csv);
        Assert.Contains("Adoção por bloco", csv);
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var reader = new StreamReader(archive.GetEntry(path)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var position = 0;
        while ((position = value.IndexOf(token, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += token.Length;
        }

        return count;
    }

    private static void WriteQaArtifact(string fileName, byte[] bytes)
    {
        var directory = Environment.GetEnvironmentVariable("CONDOTIFY_REPORT_EXPORT_SAMPLE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, fileName), bytes);
    }

    private static LicenseReportsViewModel CreateReport() => new()
    {
        GeneratedAt = new DateTime(2026, 8, 13, 15, 30, 0, DateTimeKind.Utc),
        PeriodStart = new DateTime(2026, 7, 15),
        PeriodEnd = new DateTime(2026, 8, 13),
        PeriodDays = 30,
        QualityScore = 87,
        Residents = new ResidentReportSummaryViewModel
        {
            Registered = 168,
            Active = 154,
            AccountsCreated = 142,
            AppLinked = 136,
            RecentlyActive = 119,
            WithCompleteContact = 151,
            WithDocument = 148,
            WithProfilePhoto = 128,
            WithActiveCredential = 145,
            WithFacialCredential = 103,
            AccountActivationRate = 92.21m,
            AppAdoptionRate = 88.31m,
            RecentUsageRate = 77.27m
        },
        Structure = new StructureReportSummaryViewModel
        {
            Blocks = 3,
            Units = 96,
            OccupiedUnits = 88,
            VacantUnits = 8,
            Vehicles = 121,
            OccupancyRate = 91.67m
        },
        Operation = new OperationReportSummaryViewModel
        {
            AccessEvents = 4_812,
            AuthorizedAccesses = 4_691,
            DeniedAccesses = 121,
            AuthorizationRate = 97.49m,
            VisitorsCreated = 284,
            VisitorsCheckedIn = 251,
            VisitorsPending = 21,
            VisitorsExpired = 12,
            PeakHour = 18
        },
        QualityIndicators =
        [
            new() { Key = "contact", Label = "Contato completo", Description = "Telefone e e-mail cadastrados", Value = 151, Total = 154, Percentage = 98.05m, Tone = "good" },
            new() { Key = "photo", Label = "Foto de perfil", Description = "Moradores com foto atualizada", Value = 128, Total = 154, Percentage = 83.12m, Tone = "good" }
        ],
        AdoptionByBlock =
        [
            new() { BlockName = "Torre Aurora", Units = 32, OccupiedUnits = 30, Residents = 54, AccountsCreated = 51, AppLinked = 49, AdoptionRate = 90.74m },
            new() { BlockName = "Torre Horizonte", Units = 32, OccupiedUnits = 29, Residents = 52, AccountsCreated = 48, AppLinked = 45, AdoptionRate = 86.54m },
            new() { BlockName = "Torre Jardim", Units = 32, OccupiedUnits = 29, Residents = 48, AccountsCreated = 43, AppLinked = 42, AdoptionRate = 87.50m }
        ],
        Trend =
        [
            new() { Date = new DateTime(2026, 7, 15), EndDate = new DateTime(2026, 7, 21), Authorized = 1_083, Denied = 31, Visitors = 61 },
            new() { Date = new DateTime(2026, 7, 22), EndDate = new DateTime(2026, 7, 28), Authorized = 1_125, Denied = 29, Visitors = 69 },
            new() { Date = new DateTime(2026, 7, 29), EndDate = new DateTime(2026, 8, 4), Authorized = 1_151, Denied = 33, Visitors = 73 },
            new() { Date = new DateTime(2026, 8, 5), EndDate = new DateTime(2026, 8, 13), Authorized = 1_332, Denied = 28, Visitors = 81 }
        ],
        AccessByHour = Enumerable.Range(0, 24).Select(hour => new ReportHourViewModel
        {
            Hour = hour,
            Authorized = hour is >= 7 and <= 20 ? 180 + hour * 8 : 24 + hour,
            Denied = hour is >= 7 and <= 20 ? 4 + hour % 5 : hour % 3
        }).ToList(),
        AttentionItems =
        [
            new() { Title = "Cadastros sem foto", Description = "Atualize a identificação visual dos moradores.", Count = 26, Severity = "warning" },
            new() { Title = "Acessos negados", Description = "Revise os eventos recorrentes no período.", Count = 121, Severity = "danger" }
        ]
    };
}
