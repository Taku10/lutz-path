using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace LutzPath.ResumeService.Services;

public record TemplateParagraph(int Id, string Text, bool Editable, string Reason);

// Keep the uploaded package and change only text in structurally simple paragraphs.
// Complex runs remain locked rather than guessing where formatting should move.
public static class WordTemplates
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static XDocument Read(ZipArchive zip)
    {
        if (zip.Entries.Sum(x => x.Length) > 10_000_000 || zip.Entries.Count(x => x.FullName == "word/document.xml") != 1 ||
            zip.Entries.Any(x => x.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Use a .docx without macros, smaller than 10 MB uncompressed.");
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 10_000_000 });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
    private static XElement[] Paragraphs(XDocument doc) => doc.Descendants(W + "p")
        .Where(p => !p.Ancestors(W + "p").Any() && !string.IsNullOrWhiteSpace(Text(p))).ToArray();
    private static string Text(XElement p) => string.Concat(p.Descendants(W + "t").Select(t => t.Value));
    private static bool Editable(XElement p)
    {
        if (p.Ancestors(W + "sdt").Any() || p.Ancestors(W + "del").Any() || p.Ancestors(W + "ins").Any()) return false;
        if (p.Elements().Any(e => e.Name != W + "pPr" && e.Name != W + "r" && e.Name != W + "proofErr")) return false;
        var runs = p.Elements(W + "r").ToArray();
        if (runs.Any(r => r.Elements().Any(e => e.Name != W + "rPr" && e.Name != W + "t"))) return false;
        return runs.Where(r => r.Elements(W + "t").Any())
            .Select(r => r.Element(W + "rPr")?.ToString(SaveOptions.DisableFormatting) ?? "")
            .Distinct().Count() == 1;
    }
    public static TemplateParagraph[] Describe(byte[] template)
    {
        using var stream = new MemoryStream(template);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return Paragraphs(Read(zip)).Select((p, i) => new TemplateParagraph(i + 1, Text(p), Editable(p),
            Editable(p) ? "" : "Protected layout: mixed formatting, hyperlinks, fields, tabs or other complex content.")).ToArray();
    }
    public static TemplateParagraph[] WithApprovedContent(byte[] template, string content)
    {
        var paragraphs = Describe(template);
        ValidateContent(paragraphs, content);
        var lines = Lines(content);
        if (lines.Length != paragraphs.Length)
            throw new InvalidDataException("The approved text no longer matches the Word paragraphs. Upload the corrected Word file again; keep one text line per original paragraph.");
        return paragraphs.Select((p, i) => {
            if (!p.Editable && p.Text != lines[i]) throw new InvalidDataException($"Protected paragraph {p.Id} was changed in the text editor. Correct it in Word and upload a new master instead.");
            return p with { Text = lines[i] };
        }).ToArray();
    }
    public static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');
    public static void ValidateContent(TemplateParagraph[] paragraphs, string content)
    {
        try { XmlConvert.VerifyXmlChars(content); }
        catch (XmlException) { throw new InvalidDataException("Remove invalid control characters from the resume text."); }
        var lines = Lines(content);
        if (lines.Length != paragraphs.Length || lines.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Keep one nonempty line per original paragraph. Edit wording without adding, deleting or reordering paragraphs.");
        foreach (var p in paragraphs)
            if (!p.Editable && lines[p.Id - 1] != p.Text)
                throw new InvalidDataException($"Paragraph {p.Id} has protected formatting and must stay unchanged.");
    }
    public static byte[] Apply(byte[] template, TemplateParagraph[] paragraphs, string content)
    {
        ValidateContent(paragraphs, content);
        var actual = Describe(template);
        if (actual.Length != paragraphs.Length || actual.Where((p, i) => p.Id != paragraphs[i].Id || p.Editable != paragraphs[i].Editable).Any())
            throw new InvalidDataException("The saved template does not match this draft. Generate a new draft from your master.");
        using var output = new MemoryStream();
        output.Write(template); output.Position = 0;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true))
        {
            var doc = Read(zip); var nodes = Paragraphs(doc); var lines = Lines(content);
            for (var i = 0; i < nodes.Length; i++)
            {
                if (Text(nodes[i]) == lines[i]) continue;
                if (!actual[i].Editable) throw new InvalidDataException($"Protected paragraph {i + 1} cannot be replaced.");
                var texts = nodes[i].Descendants(W + "t").ToArray();
                texts[0].Value = lines[i]; texts[0].SetAttributeValue(XNamespace.Xml + "space", "preserve");
                foreach (var text in texts.Skip(1)) text.Value = "";
            }
            var entry = zip.GetEntry("word/document.xml")!;
            using var stream = entry.Open(); stream.SetLength(0); doc.Save(stream, SaveOptions.DisableFormatting);
        }
        return output.ToArray();
    }
}
