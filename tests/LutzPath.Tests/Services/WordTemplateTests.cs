using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using LutzPath.ResumeService.Services;

namespace LutzPath.Tests.Services;

public class WordTemplateTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static byte[] Template()
    {
        using var output = new MemoryStream(); output.Write(WordDocuments.Create("Test fixture resume", "Arial")); output.Position = 0;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true))
        {
            zip.GetEntry("word/document.xml")!.Delete();
            using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open())) writer.Write("""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body>
                  <w:p><w:pPr><w:spacing w:after="100"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>EXPERIENCE</w:t></w:r><w:r><w:t> · Example Corp</w:t></w:r></w:p>
                  <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr><w:ind w:left="360"/><w:spacing w:after="40"/></w:pPr><w:r><w:rPr><w:rFonts w:ascii="Arial"/><w:sz w:val="20"/></w:rPr><w:t xml:space="preserve">Built </w:t></w:r><w:r><w:rPr><w:rFonts w:ascii="Arial"/><w:sz w:val="20"/></w:rPr><w:t>software.</w:t></w:r></w:p>
                  <w:p/>
                  <w:tbl><w:tblPr><w:tblW w:w="9000" w:type="dxa"/></w:tblPr><w:tr><w:tc><w:tcPr><w:tcW w:w="9000" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>Created a project.</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                  <w:p><w:hyperlink r:id="rIdLink"><w:r><w:rPr><w:u w:val="single"/></w:rPr><w:t>portfolio.example</w:t></w:r></w:hyperlink></w:p>
                  <w:p><w:r><w:t>Engineer</w:t><w:tab/><w:t>2026</w:t></w:r></w:p>
                  <w:sectPr><w:headerReference w:type="default" r:id="rIdHeader"/><w:footerReference w:type="default" r:id="rIdFooter"/><w:pgMar w:top="600" w:bottom="600" w:left="720" w:right="720"/></w:sectPr>
                </w:body></w:document>
                """);
            foreach (var (name, value) in new[] {
                ("word/styles.xml", "<styles>original style definitions</styles>"),
                ("word/numbering.xml", "<numbering>original bullet definitions</numbering>"),
                ("word/header1.xml", "<header>original contact details</header>"),
                ("word/footer1.xml", "<footer>original footer</footer>"),
                ("word/_rels/document.xml.rels", "<relationships>original hyperlink targets</relationships>"),
                ("word/media/image1.bin", "original image bytes") })
            { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(value); }
        }
        return output.ToArray();
    }
    private static Dictionary<string, byte[]> Parts(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e => { using var data = new MemoryStream(); using var input = e.Open(); input.CopyTo(data); return data.ToArray(); });
    }
    [Fact]
    public void ExportKeepsStylesBulletsTablesLinksHeadersFootersAndPackageParts()
    {
        var original = Template(); var before = Parts(original); var manifest = WordTemplates.Describe(original);
        Assert.Equal(new[] { false, true, true, false, false }, manifest.Select(x => x.Editable));
        var content = string.Join("\n", manifest.Select(p => p.Id switch { 2 => "Built reliable C# software & tools.", 3 => "Created a relevant project.", _ => p.Text }));
        var after = Parts(WordTemplates.Apply(original, manifest, content));
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var part in before.Keys.Where(x => x != "word/document.xml")) Assert.Equal(before[part], after[part]);
        using var originalXml = new MemoryStream(before["word/document.xml"]);
        using var updatedXml = new MemoryStream(after["word/document.xml"]);
        var oldDoc = XDocument.Load(originalXml);
        var newDoc = XDocument.Load(updatedXml);
        foreach (var name in new[] { "pPr", "rPr", "sectPr", "tblPr", "tcPr", "hyperlink" })
            Assert.Equal(oldDoc.Descendants(W + name).Select(x => x.ToString()), newDoc.Descendants(W + name).Select(x => x.ToString()));
        Assert.Contains("Built reliable C# software &amp; tools.", newDoc.ToString());
        Assert.Equal(before["word/document.xml"], Parts(original)["word/document.xml"]);
    }
    [Fact]
    public void RejectsParagraphCountChangesAndProtectedContentEdits()
    {
        var bytes = Template(); var manifest = WordTemplates.Describe(bytes);
        Assert.Throws<InvalidDataException>(() => WordTemplates.Apply(bytes, manifest, "A rewritten resume with no mapping"));
        var lines = manifest.Select(p => p.Text).ToArray(); lines[0] = "Changed mixed-format heading";
        Assert.Throws<InvalidDataException>(() => WordTemplates.Apply(bytes, manifest, string.Join("\n", lines)));
        Assert.Throws<InvalidDataException>(() => WordTemplates.WithApprovedContent(bytes, string.Join("\n", lines)));
    }
    [Fact]
    public void RejectsMissingDuplicateAndProtectedAiReplacementIds()
    {
        var template = WordTemplates.Describe(Template());
        Assert.Throws<InvalidOperationException>(() => TailoringClient.ValidateTemplate(new([new("Rewrite", [2], 2)], ""), template));
        Assert.Throws<InvalidOperationException>(() => TailoringClient.ValidateTemplate(new([new("Rewrite", [2], 2), new("Duplicate", [2], 2)], ""), template));
        Assert.Throws<InvalidOperationException>(() => TailoringClient.ValidateTemplate(new([new("Wrong", [1], 1), new("Rewrite", [3], 3)], ""), template));
        TailoringClient.ValidateTemplate(new([new("Rewrite", [2], 2), new("Rewrite project", [3], 3)], ""), template);
    }
}
