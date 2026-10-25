using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
namespace LutzPath.ResumeService.Services;

// Plain document helper for legacy drafts and test fixtures. New exports use WordTemplates.
public static class WordDocuments
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static string Extract(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, true);
        if (zip.Entries.Sum(x => x.Length) > 10_000_000 || zip.Entries.Any(x => x.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Upload a document smaller than 10 MB uncompressed, without macros.");
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("This is not a Word .docx file.");
        using var input = entry.Open();
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 10_000_000 });
        var doc = XDocument.Load(reader);
        var text = string.Join("\n", doc.Descendants(W + "p").Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value))).Where(x => !string.IsNullOrWhiteSpace(x)));
        if (text.Length is < 10 or > 60000) throw new InvalidDataException("The resume must contain 10–60,000 characters of selectable text.");
        return text;
    }
    public static byte[] Create(string text, string font)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Write(string path, string value) { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write(value); }
            Write("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Write("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            var paragraphs = text.Split('\n').Select(line => new XElement(W + "p", new XElement(W + "r",
                new XElement(W + "rPr", new XElement(W + "rFonts", new XAttribute(W + "ascii", font), new XAttribute(W + "hAnsi", font)), new XElement(W + "sz", new XAttribute(W + "val", "22"))),
                new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), line))));
            var body = new XElement(W + "body", paragraphs, new XElement(W + "sectPr", new XElement(W + "pgSz", new XAttribute(W + "w", "12240"), new XAttribute(W + "h", "15840")), new XElement(W + "pgMar", new XAttribute(W + "top", "720"), new XAttribute(W + "bottom", "720"), new XAttribute(W + "left", "900"), new XAttribute(W + "right", "900"))));
            Write("word/document.xml", new XDocument(new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), body)).ToString());
        }
        return output.ToArray();
    }
}
