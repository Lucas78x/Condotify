using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using Condotify.Models;

namespace CondotifyAPI.Services.Reports;

public static class ReportExportService
{
    private const string Navy = "12366B";
    private const string Blue = "3156D3";
    private const string Green = "0F927B";
    private const string Amber = "D58B10";
    private const string Red = "D94E63";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static byte[] CreateCsv(LicenseReportsViewModel report, string licenseName, string licenseCode)
    {
        var csv = new StringBuilder("\uFEFF");
        CsvRow(csv, "F&F Access - Relatório do condomínio");
        CsvRow(csv, "Condomínio", licenseName);
        CsvRow(csv, "Código", licenseCode);
        CsvRow(csv, "Período", $"{report.PeriodStart:dd/MM/yyyy} a {report.PeriodEnd:dd/MM/yyyy}");
        CsvRow(csv, "Gerado em", report.GeneratedAt.ToCondotifyTime().ToString("dd/MM/yyyy HH:mm"));
        csv.AppendLine();
        CsvRow(csv, "Categoria", "Indicador", "Valor", "Detalhe");
        CsvRow(csv, "Resumo", "Índice de qualidade", report.QualityScore.ToString(PtBr), "Escala de 0 a 100");
        CsvRow(csv, "Moradores e aplicativo", "Moradores ativos", report.Residents.Active.ToString(PtBr), $"{report.Residents.Registered} cadastrados");
        CsvRow(csv, "Moradores e aplicativo", "Contas criadas", report.Residents.AccountsCreated.ToString(PtBr), Percent(report.Residents.AccountActivationRate));
        CsvRow(csv, "Moradores e aplicativo", "Aplicativo vinculado", report.Residents.AppLinked.ToString(PtBr), Percent(report.Residents.AppAdoptionRate));
        CsvRow(csv, "Moradores e aplicativo", "Uso recente", report.Residents.RecentlyActive.ToString(PtBr), Percent(report.Residents.RecentUsageRate));
        CsvRow(csv, "Estrutura", "Unidades ocupadas", report.Structure.OccupiedUnits.ToString(PtBr), $"{report.Structure.Units} unidades cadastradas");
        CsvRow(csv, "Operação", "Acessos", report.Operation.AccessEvents.ToString(PtBr), $"{report.Operation.AuthorizedAccesses} autorizados; {report.Operation.DeniedAccesses} negados");
        CsvRow(csv, "Operação", "Visitantes", report.Operation.VisitorsCreated.ToString(PtBr), $"{report.Operation.VisitorsCheckedIn} com entrada registrada");
        foreach (var indicator in report.QualityIndicators)
            CsvRow(csv, "Qualidade cadastral", indicator.Label, indicator.Value.ToString(PtBr), $"{Percent(indicator.Percentage)} de {indicator.Total}");
        foreach (var block in report.AdoptionByBlock)
            CsvRow(csv, "Adoção por bloco", block.BlockName, block.AccountsCreated.ToString(PtBr), $"{block.Residents} moradores; {block.AppLinked} apps vinculados; {Percent(block.AdoptionRate)}");
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    public static byte[] CreateExcel(LicenseReportsViewModel report, string licenseName, string licenseCode)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(archive, "[Content_Types].xml", ContentTypesXml);
            AddText(archive, "_rels/.rels", RootRelationshipsXml);
            AddText(archive, "docProps/core.xml", CorePropertiesXml(report.GeneratedAt));
            AddText(archive, "docProps/app.xml", AppPropertiesXml);
            AddText(archive, "xl/workbook.xml", WorkbookXml);
            AddText(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml);
            AddText(archive, "xl/styles.xml", StylesXml);
            AddText(archive, "xl/worksheets/sheet1.xml", SummarySheet(report, licenseName, licenseCode));
            AddText(archive, "xl/worksheets/sheet2.xml", QualitySheet(report));
            AddText(archive, "xl/worksheets/sheet3.xml", AdoptionSheet(report));
            AddText(archive, "xl/worksheets/sheet4.xml", OperationSheet(report));
        }
        return stream.ToArray();
    }

    public static byte[] CreatePdf(LicenseReportsViewModel report, string licenseName, string licenseCode)
    {
        var pdf = new SimplePdf();
        var page = pdf.AddPage();
        DrawHeader(page, "RELATÓRIO EXECUTIVO", licenseName, licenseCode);
        page.Text(50, 128, 10, "Período analisado", Navy, bold: true);
        page.Text(50, 145, 11, $"{report.PeriodStart:dd/MM/yyyy} a {report.PeriodEnd:dd/MM/yyyy} - {report.PeriodDays} dias", "52637A");
        page.Text(390, 128, 10, "Gerado em", Navy, bold: true);
        page.Text(390, 145, 11, report.GeneratedAt.ToCondotifyTime().ToString("dd/MM/yyyy HH:mm"), "52637A");

        DrawKpi(page, 50, 180, 118, 92, "QUALIDADE", report.QualityScore.ToString(PtBr), "de 100", Blue);
        DrawKpi(page, 177, 180, 118, 92, "MORADORES", report.Residents.Active.ToString(PtBr), "ativos", Green);
        DrawKpi(page, 304, 180, 118, 92, "CONTAS APP", report.Residents.AccountsCreated.ToString(PtBr), Percent(report.Residents.AccountActivationRate), "7554C9");
        DrawKpi(page, 431, 180, 118, 92, "ACESSOS", report.Operation.AccessEvents.ToString(PtBr), Percent(report.Operation.AuthorizationRate) + " autorizados", Amber);

        DrawSectionTitle(page, 50, 304, "Resumo do condomínio");
        var summary = new[]
        {
            ("Unidades", report.Structure.Units.ToString(PtBr), $"{report.Structure.OccupiedUnits} ocupadas"),
            ("Aplicativos vinculados", report.Residents.AppLinked.ToString(PtBr), Percent(report.Residents.AppAdoptionRate)),
            ("Credenciais ativas", report.Residents.WithActiveCredential.ToString(PtBr), $"{report.Residents.WithFacialCredential} faciais"),
            ("Visitantes", report.Operation.VisitorsCreated.ToString(PtBr), $"{report.Operation.VisitorsCheckedIn} entradas"),
            ("Acessos negados", report.Operation.DeniedAccesses.ToString(PtBr), "eventos para revisar")
        };
        var top = 340d;
        foreach (var item in summary)
        {
            page.Line(50, top + 31, 549, top + 31, "DFE6F0");
            page.Text(58, top, 10, item.Item1, "52637A");
            page.Text(310, top, 11, item.Item2, Navy, bold: true);
            page.Text(390, top, 9, item.Item3, "6F7F95");
            top += 40;
        }

        DrawSectionTitle(page, 50, 570, "Pontos de atenção");
        if (report.AttentionItems.Count == 0)
        {
            page.Box(50, 607, 499, 54, "EAF7F3", "B8E2D6");
            page.Text(66, 626, 12, "Tudo em dia", Green, bold: true);
            page.Text(66, 645, 9, "Nenhuma pendência relevante foi identificada no período.", "52637A");
        }
        else
        {
            top = 607;
            foreach (var item in report.AttentionItems.Take(3))
            {
                var tone = item.Severity == "critical" ? Red : item.Severity == "warning" ? Amber : Blue;
                page.Box(50, top, 499, 48, "F8FAFD", "DFE6F0");
                page.Box(50, top, 5, 48, tone, tone);
                page.Text(68, top + 13, 10, item.Title, Navy, bold: true);
                page.Text(468, top + 13, 13, item.Count.ToString(PtBr), tone, bold: true);
                page.Text(68, top + 31, 8, Truncate(item.Description, 78), "6F7F95");
                top += 56;
            }
        }
        DrawFooter(page, 1);

        page = pdf.AddPage();
        DrawHeader(page, "QUALIDADE CADASTRAL", licenseName, licenseCode);
        DrawSectionTitle(page, 50, 130, "Cobertura e consistência dos cadastros");
        DrawTableHeader(page, 50, 166, ["Indicador", "Resultado", "Cobertura", "Situação"], [260, 80, 82, 77]);
        top = 196;
        foreach (var item in report.QualityIndicators)
        {
            var tone = item.Tone == "good" ? Green : item.Tone == "attention" ? Amber : Red;
            page.Text(58, top + 8, 10, item.Label, Navy, bold: true);
            page.Text(58, top + 25, 8, Truncate(item.Description, 55), "6F7F95");
            page.Text(318, top + 15, 10, $"{item.Value} de {item.Total}", Navy);
            page.Text(398, top + 15, 10, Percent(item.Percentage), tone, bold: true);
            page.Text(480, top + 15, 9, StatusLabel(item.Tone), tone, bold: true);
            page.Line(50, top + 43, 549, top + 43, "DFE6F0");
            top += 48;
        }
        DrawSectionTitle(page, 50, top + 25, "Adoção digital por bloco");
        top += 61;
        DrawTableHeader(page, 50, top, ["Bloco", "Moradores", "Contas", "Apps", "Adoção"], [215, 72, 70, 70, 72]);
        top += 30;
        foreach (var block in report.AdoptionByBlock.Take(8))
        {
            page.Text(58, top + 9, 9, Truncate(block.BlockName, 32), Navy, bold: true);
            page.Text(273, top + 9, 9, block.Residents.ToString(PtBr), "52637A");
            page.Text(345, top + 9, 9, block.AccountsCreated.ToString(PtBr), "52637A");
            page.Text(415, top + 9, 9, block.AppLinked.ToString(PtBr), "52637A");
            page.Text(485, top + 9, 9, Percent(block.AdoptionRate), Green, bold: true);
            page.Line(50, top + 28, 549, top + 28, "DFE6F0");
            top += 31;
        }
        DrawFooter(page, 2);

        page = pdf.AddPage();
        DrawHeader(page, "OPERAÇÃO E MOVIMENTO", licenseName, licenseCode);
        DrawSectionTitle(page, 50, 130, "Evolução no período");
        DrawTableHeader(page, 50, 166, ["Período", "Autorizados", "Negados", "Visitantes"], [205, 98, 98, 98]);
        top = 196;
        foreach (var point in report.Trend.Take(14))
        {
            var period = point.Date == point.EndDate ? point.Date.ToString("dd/MM/yyyy") : $"{point.Date:dd/MM} a {point.EndDate:dd/MM/yyyy}";
            page.Text(58, top + 9, 9, period, Navy, bold: true);
            page.Text(263, top + 9, 9, point.Authorized.ToString(PtBr), Green);
            page.Text(361, top + 9, 9, point.Denied.ToString(PtBr), Red);
            page.Text(459, top + 9, 9, point.Visitors.ToString(PtBr), Blue);
            page.Line(50, top + 27, 549, top + 27, "DFE6F0");
            top += 30;
        }
        DrawSectionTitle(page, 50, Math.Min(top + 24, 650), "Leitura executiva");
        top = Math.Min(top + 62, 688);
        page.Box(50, top, 499, 70, "EEF3FF", "D5DFF8");
        page.Text(66, top + 18, 11, $"{Percent(report.Operation.AuthorizationRate)} dos acessos foram autorizados.", Navy, bold: true);
        page.Text(66, top + 39, 9, $"Maior movimento entre {report.Operation.PeakHour:00}h e {(report.Operation.PeakHour + 1) % 24:00}h. {report.Operation.VisitorsPending} visitante(s) pendente(s).", "52637A");
        DrawFooter(page, 3);

        return pdf.Build();
    }

    private static string SummarySheet(LicenseReportsViewModel r, string name, string code)
    {
        var rows = new List<string>
        {
            Row(1, Cell("A1", "CONDOTIFY - RELATÓRIO EXECUTIVO", 1)),
            Row(3, Cell("A3", name, 2), Cell("G3", code, 2)),
            Row(4, Cell("A4", $"Período: {r.PeriodStart:dd/MM/yyyy} a {r.PeriodEnd:dd/MM/yyyy}", 13), Cell("G4", $"Gerado em {r.GeneratedAt.ToCondotifyTime():dd/MM/yyyy HH:mm}", 13)),
            Row(6, Cell("A6", "ÍNDICE DE QUALIDADE", 11), Cell("C6", "MORADORES ATIVOS", 11), Cell("E6", "CONTAS NO APP", 11), Cell("G6", "ACESSOS NO PERÍODO", 11)),
            Row(7, NumberCell("A7", r.QualityScore, 12), NumberCell("C7", r.Residents.Active, 12), NumberCell("E7", r.Residents.AccountsCreated, 12), NumberCell("G7", r.Operation.AccessEvents, 12)),
            Row(8, Cell("A8", "de 100", 13), Cell("C8", $"{r.Residents.Registered} cadastrados", 13), PercentCell("E8", r.Residents.AccountActivationRate, 7), PercentCell("G8", r.Operation.AuthorizationRate, 7)),
            Row(10, Cell("A10", "MORADORES E APLICATIVO", 3)),
            Row(11, Cell("A11", "Indicador", 4), Cell("B11", "Valor", 4), Cell("C11", "Cobertura", 4), Cell("D11", "Detalhe", 4)),
            DataRow(12, "Moradores ativos", r.Residents.Active, 100, $"{r.Residents.Registered} cadastrados"),
            DataRow(13, "Contas criadas", r.Residents.AccountsCreated, r.Residents.AccountActivationRate, "Acesso preparado"),
            DataRow(14, "Aplicativo vinculado", r.Residents.AppLinked, r.Residents.AppAdoptionRate, "Dispositivo identificado"),
            DataRow(15, "Uso recente", r.Residents.RecentlyActive, r.Residents.RecentUsageRate, "Sessão nos últimos 30 dias"),
            Row(17, Cell("A17", "ESTRUTURA E OPERAÇÃO", 3)),
            Row(18, Cell("A18", "Indicador", 4), Cell("B18", "Valor", 4), Cell("C18", "Cobertura", 4), Cell("D18", "Detalhe", 4)),
            DataRow(19, "Unidades ocupadas", r.Structure.OccupiedUnits, r.Structure.OccupancyRate, $"{r.Structure.Units} unidades"),
            DataRow(20, "Credenciais ativas", r.Residents.WithActiveCredential, Percentage(r.Residents.WithActiveCredential, r.Residents.Active), $"{r.Residents.WithFacialCredential} faciais"),
            DataRow(21, "Acessos autorizados", r.Operation.AuthorizedAccesses, r.Operation.AuthorizationRate, $"{r.Operation.DeniedAccesses} negados"),
            DataRow(22, "Visitantes", r.Operation.VisitorsCreated, Percentage(r.Operation.VisitorsCheckedIn, r.Operation.VisitorsCreated), $"{r.Operation.VisitorsCheckedIn} entradas")
        };
        return SheetXml(rows, "<col min=\"1\" max=\"1\" width=\"27\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"14\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"18\" customWidth=\"1\"/><col min=\"4\" max=\"8\" width=\"18\" customWidth=\"1\"/>", "A1:H2 A3:F3 G3:H3 A4:F4 G4:H4 A6:B6 C6:D6 E6:F6 G6:H6 A7:B7 C7:D7 E7:F7 G7:H7 A8:B8 C8:D8 E8:F8 G8:H8 A10:H10 A17:H17", "A11:D15", 11);
    }

    private static string QualitySheet(LicenseReportsViewModel r)
    {
        var rows = new List<string> { Row(1, Cell("A1", "QUALIDADE CADASTRAL", 1)), Row(3, Cell("A3", "Indicador", 4), Cell("B3", "Descrição", 4), Cell("C3", "Resultado", 4), Cell("D3", "Total", 4), Cell("E3", "Cobertura", 4), Cell("F3", "Situação", 4)) };
        var row = 4;
        foreach (var item in r.QualityIndicators)
        {
            var statusStyle = item.Tone == "good" ? 8 : item.Tone == "attention" ? 9 : 10;
            rows.Add(Row(row, Cell($"A{row}", item.Label, 5), Cell($"B{row}", item.Description, 5), NumberCell($"C{row}", item.Value, 6), NumberCell($"D{row}", item.Total, 6), PercentCell($"E{row}", item.Percentage, 7), Cell($"F{row}", StatusLabel(item.Tone), statusStyle)));
            row++;
        }
        return SheetXml(rows, "<col min=\"1\" max=\"1\" width=\"26\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"55\" customWidth=\"1\"/><col min=\"3\" max=\"4\" width=\"13\" customWidth=\"1\"/><col min=\"5\" max=\"6\" width=\"16\" customWidth=\"1\"/>", "A1:F2", $"A3:F{Math.Max(3, row - 1)}", 3);
    }

    private static string AdoptionSheet(LicenseReportsViewModel r)
    {
        var rows = new List<string> { Row(1, Cell("A1", "ADOÇÃO DIGITAL POR BLOCO", 1)), Row(3, Cell("A3", "Bloco", 4), Cell("B3", "Unidades", 4), Cell("C3", "Ocupadas", 4), Cell("D3", "Moradores", 4), Cell("E3", "Contas", 4), Cell("F3", "Apps", 4), Cell("G3", "Adoção", 4)) };
        var row = 4;
        foreach (var item in r.AdoptionByBlock)
        {
            rows.Add(Row(row, Cell($"A{row}", item.BlockName, 5), NumberCell($"B{row}", item.Units, 6), NumberCell($"C{row}", item.OccupiedUnits, 6), NumberCell($"D{row}", item.Residents, 6), NumberCell($"E{row}", item.AccountsCreated, 6), NumberCell($"F{row}", item.AppLinked, 6), PercentCell($"G{row}", item.AdoptionRate, 7)));
            row++;
        }
        return SheetXml(rows, "<col min=\"1\" max=\"1\" width=\"32\" customWidth=\"1\"/><col min=\"2\" max=\"7\" width=\"15\" customWidth=\"1\"/>", "A1:G2", $"A3:G{Math.Max(3, row - 1)}", 3);
    }

    private static string OperationSheet(LicenseReportsViewModel r)
    {
        var rows = new List<string> { Row(1, Cell("A1", "OPERAÇÃO E MOVIMENTO", 1)), Row(3, Cell("A3", "Período", 4), Cell("B3", "Autorizados", 4), Cell("C3", "Negados", 4), Cell("D3", "Visitantes", 4)) };
        var row = 4;
        foreach (var item in r.Trend)
        {
            var period = item.Date == item.EndDate ? item.Date.ToString("dd/MM/yyyy") : $"{item.Date:dd/MM/yyyy} a {item.EndDate:dd/MM/yyyy}";
            rows.Add(Row(row, Cell($"A{row}", period, 5), NumberCell($"B{row}", item.Authorized, 6), NumberCell($"C{row}", item.Denied, 6), NumberCell($"D{row}", item.Visitors, 6)));
            row++;
        }
        row += 2;
        rows.Add(Row(row, Cell($"A{row}", "MOVIMENTO POR HORÁRIO", 3))); row++;
        rows.Add(Row(row, Cell($"A{row}", "Horário", 4), Cell($"B{row}", "Autorizados", 4), Cell($"C{row}", "Negados", 4), Cell($"D{row}", "Total", 4))); var hourHeader = row; row++;
        foreach (var item in r.AccessByHour)
        {
            rows.Add(Row(row, Cell($"A{row}", $"{item.Hour:00}:00", 5), NumberCell($"B{row}", item.Authorized, 6), NumberCell($"C{row}", item.Denied, 6), NumberCell($"D{row}", item.Total, 6)));
            row++;
        }
        return SheetXml(rows, "<col min=\"1\" max=\"1\" width=\"28\" customWidth=\"1\"/><col min=\"2\" max=\"4\" width=\"17\" customWidth=\"1\"/>", "A1:D2", $"A3:D{Math.Max(3, hourHeader - 3)}", 3);
    }

    private static string SheetXml(IEnumerable<string> rows, string columns, string merges, string autoFilter, int freezeRow)
    {
        var mergeItems = merges.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => $"<mergeCell ref=\"{x}\"/>");
        return $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetViews><sheetView showGridLines="0" workbookViewId="0"><pane ySplit="{freezeRow - 1}" topLeftCell="A{freezeRow}" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight="20"/><cols>{columns}</cols><sheetData>{string.Join(string.Empty, rows)}</sheetData><autoFilter ref="{autoFilter}"/><mergeCells count="{mergeItems.Count()}">{string.Join(string.Empty, mergeItems)}</mergeCells><pageMargins left="0.4" right="0.4" top="0.6" bottom="0.6" header="0.2" footer="0.2"/><pageSetup orientation="landscape" fitToWidth="1" fitToHeight="0"/></worksheet>""";
    }

    private static string DataRow(int row, string label, int value, decimal percentage, string detail) => Row(row, Cell($"A{row}", label, 5), NumberCell($"B{row}", value, 6), PercentCell($"C{row}", percentage, 7), Cell($"D{row}", detail, 5));
    private static string Row(int index, params string[] cells) => $"<row r=\"{index}\" ht=\"{(index is 1 or 2 ? 28 : 21)}\" customHeight=\"1\">{string.Join(string.Empty, cells)}</row>";
    private static string Cell(string reference, string value, int style = 0) => $"<c r=\"{reference}\" s=\"{style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlEscape(value)}</t></is></c>";
    private static string NumberCell(string reference, decimal value, int style = 6) => $"<c r=\"{reference}\" s=\"{style}\"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c>";
    private static string PercentCell(string reference, decimal value, int style = 7) => NumberCell(reference, value / 100m, style);
    private static string XmlEscape(string value) => SecurityElement.Escape(value) ?? string.Empty;
    private static decimal Percentage(int value, int total) => total <= 0 ? 0 : Math.Round(value * 100m / total, 1);
    private static string Percent(decimal value) => value.ToString("0.#", PtBr) + "%";
    private static string StatusLabel(string tone) => tone switch { "good" => "Adequado", "attention" => "Atenção", "critical" => "Crítico", _ => "Informativo" };
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..Math.Max(1, length - 3)] + "...";
    private static void CsvRow(StringBuilder builder, params string[] fields) => builder.AppendLine(string.Join(";", fields.Select(x => $"\"{(x ?? string.Empty).Replace("\"", "\"\"")}\"")));

    private static void AddText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void DrawHeader(PdfPageBuilder page, string eyebrow, string name, string code)
    {
        page.Box(0, 0, 595, 105, Navy, Navy);
        page.Text(50, 28, 9, "CONDOTIFY", "9BC0FF", bold: true);
        page.Text(50, 48, 23, name, "FFFFFF", bold: true);
        page.Text(50, 78, 10, $"{eyebrow}  •  {code}", "D8E5FF", bold: true);
    }

    private static void DrawKpi(PdfPageBuilder page, double x, double y, double width, double height, string label, string value, string detail, string tone)
    {
        page.Box(x, y, width, height, "F8FAFD", "DFE6F0");
        page.Box(x, y, 5, height, tone, tone);
        page.Text(x + 16, y + 18, 8, label, "6F7F95", bold: true);
        page.Text(x + 16, y + 42, 22, value, Navy, bold: true);
        page.Text(x + 16, y + 70, 8, detail, "52637A");
    }

    private static void DrawSectionTitle(PdfPageBuilder page, double x, double y, string title)
    {
        page.Text(x, y, 14, title, Navy, bold: true);
        page.Line(x, y + 24, 549, y + 24, "CBD7E7");
    }

    private static void DrawTableHeader(PdfPageBuilder page, double x, double y, string[] labels, double[] widths)
    {
        var current = x;
        for (var i = 0; i < labels.Length; i++)
        {
            page.Box(current, y, widths[i], 28, Blue, Blue);
            page.Text(current + 8, y + 9, 8, labels[i].ToUpperInvariant(), "FFFFFF", bold: true);
            current += widths[i];
        }
    }

    private static void DrawFooter(PdfPageBuilder page, int pageNumber)
    {
        page.Line(50, 803, 549, 803, "DFE6F0");
        page.Text(50, 815, 7, "CONDOTIFY  •  RELATÓRIO GERENCIAL", "8290A4", bold: true);
        page.Text(530, 815, 7, pageNumber.ToString("00"), "8290A4", bold: true);
    }

    private const string ContentTypesXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet3.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet4.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/><Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/></Types>""";
    private const string RootRelationshipsXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/></Relationships>""";
    private const string WorkbookXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><bookViews><workbookView activeTab="0"/></bookViews><sheets><sheet name="Resumo executivo" sheetId="1" r:id="rId1"/><sheet name="Qualidade cadastral" sheetId="2" r:id="rId2"/><sheet name="Adoção por bloco" sheetId="3" r:id="rId3"/><sheet name="Operação" sheetId="4" r:id="rId4"/></sheets><calcPr calcId="191029"/></workbook>""";
    private const string WorkbookRelationshipsXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml"/><Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet4.xml"/><Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""";
    private const string AppPropertiesXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"><Application>F&amp;F Access</Application><Company>F&amp;F Access</Company></Properties>""";
    private static string CorePropertiesXml(DateTime generatedAt) => $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:title>Relatório F&amp;F Access</dc:title><dc:creator>F&amp;F Access</dc:creator><cp:lastModifiedBy>F&amp;F Access</cp:lastModifiedBy><dcterms:created xsi:type="dcterms:W3CDTF">{generatedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}</dcterms:created></cp:coreProperties>""";
    private const string StylesXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="6"><font><sz val="11"/><color rgb="FF182235"/><name val="Aptos"/></font><font><b/><sz val="20"/><color rgb="FFFFFFFF"/><name val="Aptos Display"/></font><font><b/><sz val="12"/><color rgb="FF12366B"/><name val="Aptos"/></font><font><b/><sz val="10"/><color rgb="FFFFFFFF"/><name val="Aptos"/></font><font><b/><sz val="9"/><color rgb="FF6F7F95"/><name val="Aptos"/></font><font><b/><sz val="18"/><color rgb="FF12366B"/><name val="Aptos Display"/></font></fonts><fills count="8"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF12366B"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEAF0FF"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FF3156D3"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEAF7F3"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFFFF5DF"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFFFE9EE"/></patternFill></fill></fills><borders count="2"><border/><border><left style="thin"><color rgb="FFDFE6F0"/></left><right style="thin"><color rgb="FFDFE6F0"/></right><top style="thin"><color rgb="FFDFE6F0"/></top><bottom style="thin"><color rgb="FFDFE6F0"/></bottom></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="14"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="center"/></xf><xf numFmtId="0" fontId="2" fillId="3" borderId="0" xfId="0"/><xf numFmtId="0" fontId="2" fillId="3" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="center"/></xf><xf numFmtId="0" fontId="3" fillId="4" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf><xf numFmtId="3" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf><xf numFmtId="10" fontId="2" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf><xf numFmtId="10" fontId="2" fillId="5" borderId="1" xfId="0"/><xf numFmtId="10" fontId="2" fillId="6" borderId="1" xfId="0"/><xf numFmtId="10" fontId="2" fillId="7" borderId="1" xfId="0"/><xf numFmtId="0" fontId="4" fillId="0" borderId="0" xfId="0"/><xf numFmtId="3" fontId="5" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="4" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="right"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""";

    private sealed class SimplePdf
    {
        private readonly List<PdfPageBuilder> _pages = [];
        public PdfPageBuilder AddPage() { var page = new PdfPageBuilder(); _pages.Add(page); return page; }

        public byte[] Build()
        {
            var objects = new List<byte[]> { Array.Empty<byte>(), Latin("<< /Type /Catalog /Pages 2 0 R >>"), Array.Empty<byte>(), Latin("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"), Latin("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>") };
            var kids = new List<string>();
            for (var index = 0; index < _pages.Count; index++)
            {
                var pageObject = 5 + index * 2;
                var contentObject = pageObject + 1;
                kids.Add($"{pageObject} 0 R");
                objects.Add(Latin($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObject} 0 R >>"));
                var content = Latin(_pages[index].Content);
                objects.Add(Concat(Latin($"<< /Length {content.Length} >>\nstream\n"), content, Latin("\nendstream")));
            }
            objects[2] = Latin($"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {_pages.Count} >>");

            using var output = new MemoryStream();
            output.Write(Latin("%PDF-1.4\n%âãÏÓ\n"));
            var offsets = new List<long> { 0 };
            for (var i = 1; i < objects.Count; i++)
            {
                offsets.Add(output.Position);
                output.Write(Latin($"{i} 0 obj\n")); output.Write(objects[i]); output.Write(Latin("\nendobj\n"));
            }
            var xref = output.Position;
            output.Write(Latin($"xref\n0 {objects.Count}\n0000000000 65535 f \n"));
            for (var i = 1; i < offsets.Count; i++) output.Write(Latin($"{offsets[i]:0000000000} 00000 n \n"));
            output.Write(Latin($"trailer\n<< /Size {objects.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
            return output.ToArray();
        }

        private static byte[] Latin(string value) => Encoding.Latin1.GetBytes(value);
        private static byte[] Concat(params byte[][] arrays) { var length = arrays.Sum(x => x.Length); var result = new byte[length]; var offset = 0; foreach (var item in arrays) { Buffer.BlockCopy(item, 0, result, offset, item.Length); offset += item.Length; } return result; }
    }

    private sealed class PdfPageBuilder
    {
        private readonly StringBuilder _content = new();
        public string Content => _content.ToString();
        public void Text(double x, double top, double size, string value, string color, bool bold = false) => _content.Append($"BT /{(bold ? "F2" : "F1")} {F(size)} Tf {Rgb(color)} rg 1 0 0 1 {F(x)} {F(842 - top - size)} Tm ({PdfEscape(value)}) Tj ET\n");
        public void Box(double x, double top, double width, double height, string fill, string stroke) => _content.Append($"q {Rgb(fill)} rg {Rgb(stroke)} RG 0.7 w {F(x)} {F(842 - top - height)} {F(width)} {F(height)} re B Q\n");
        public void Line(double x1, double top1, double x2, double top2, string color) => _content.Append($"q {Rgb(color)} RG 0.7 w {F(x1)} {F(842 - top1)} m {F(x2)} {F(842 - top2)} l S Q\n");
        private static string PdfEscape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("•", "-").Replace("–", "-");
        private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Rgb(string hex) => string.Join(' ', Enumerable.Range(0, 3).Select(i => (Convert.ToInt32(hex.Substring(i * 2, 2), 16) / 255d).ToString("0.###", CultureInfo.InvariantCulture)));
    }
}
