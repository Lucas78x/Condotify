using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;

namespace CondotifyAPI.Services.Finance;

public interface IBoletoPdfProcessor
{
    int CountPages(byte[] pdfBytes);
    string ExtractPageText(byte[] pdfBytes, int pageNumber);
    byte[] ExtractPageAsPdf(byte[] pdfBytes, int pageNumber);
}

public sealed class BoletoPdfProcessor : IBoletoPdfProcessor
{
    public int CountPages(byte[] pdfBytes)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        return document.NumberOfPages;
    }

    public string ExtractPageText(byte[] pdfBytes, int pageNumber)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        return document.GetPage(pageNumber).Text;
    }

    public byte[] ExtractPageAsPdf(byte[] pdfBytes, int pageNumber)
    {
        using var input = new MemoryStream(pdfBytes);
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        using var output = new PdfSharp.Pdf.PdfDocument();
        output.AddPage(source.Pages[pageNumber - 1]);
        using var buffer = new MemoryStream();
        output.Save(buffer, closeStream: false);
        return buffer.ToArray();
    }
}
