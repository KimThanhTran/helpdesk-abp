using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Helpdesk.Export;

public class XlsxWriterTests
{
    private static XlsxContents Roundtrip(params XlsxSheet[] sheets) => XlsxContents.Read(XlsxWriter.Write(sheets));

    private static XlsxSheet Sheet(string name, params object?[][] rows)
    {
        var sheet = new XlsxSheet(name, new[] { "A", "B" });
        sheet.Rows.AddRange(rows);
        return sheet;
    }

    [Fact]
    public void Writes_the_header_then_the_rows_with_text_numbers_and_empty_cells()
    {
        var contents = Roundtrip(Sheet("Tickets", new object?[] { "HD-1", 5 }, new object?[] { null, 2.5 }));

        var rows = contents.Sheets["Tickets"];
        rows[0].ShouldBe(new object?[] { "A", "B" });
        rows[1].ShouldBe(new object?[] { "HD-1", 5d });
        rows[2].ShouldBe(new object?[] { null, 2.5 });
    }

    [Fact]
    public void Every_xml_part_is_well_formed_and_the_package_has_what_excel_needs()
    {
        var bytes = XlsxWriter.Write(new[] { Sheet("One"), Sheet("Two") });

        using var zip = new ZipArchive(new MemoryStream(bytes));
        zip.Entries.Select(e => e.FullName).ShouldBe(
            new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml", "xl/worksheets/sheet2.xml" },
            ignoreOrder: true);
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            Should.NotThrow(() => XDocument.Load(stream), entry.FullName);
        }
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil.example\",\"click\")")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1+1)")]
    public void Text_that_looks_like_a_formula_stays_text(string text)
    {
        var contents = Roundtrip(Sheet("Tickets", new object?[] { text, 1 }));

        contents.Sheets["Tickets"][1][0].ShouldBe(text);
        contents.Parts.Where(p => p.Key.StartsWith("xl/worksheets/")).ShouldAllBe(p => !p.Value.Contains("<f>") && !p.Value.Contains("<f "));
    }

    [Fact]
    public void Markup_quotes_and_unicode_survive_and_control_characters_are_dropped()
    {
        var text = "<b>\"Tom\" & 'Jerry'</b> Tiếng Việt 🎫\u0000\u0008\u001F end\twith\ttabs\r\nand a break";

        var cell = Roundtrip(Sheet("Tickets", new object?[] { text, null })).Sheets["Tickets"][1][0];

        // an XML reader turns the CR LF pair into a single line feed, which is what Excel shows too
        cell.ShouldBe("<b>\"Tom\" & 'Jerry'</b> Tiếng Việt 🎫 end\twith\ttabs\nand a break");
    }

    [Fact]
    public void A_cell_is_cut_at_the_limit_excel_accepts()
    {
        var cell = (string)Roundtrip(Sheet("Tickets", new object?[] { new string('x', 40000), null })).Sheets["Tickets"][1][0]!;

        cell.Length.ShouldBe(32767);
    }

    [Fact]
    public void A_time_is_written_as_a_real_excel_date()
    {
        var time = new DateTime(2026, 11, 2, 12, 30, 0, DateTimeKind.Utc);

        var contents = Roundtrip(Sheet("Tickets", new object?[] { time, null }));

        DateTime.FromOADate((double)contents.Sheets["Tickets"][1][0]!).ShouldBe(time); // same wall-clock time, no zone shift
        contents.Parts["xl/worksheets/sheet1.xml"].ShouldContain("s=\"2\"");
        contents.Parts["xl/styles.xml"].ShouldContain("yyyy");
    }

    [Fact]
    public void A_date_Excel_cannot_show_is_left_empty_instead_of_failing_the_whole_file()
    {
        var contents = Roundtrip(Sheet("Tickets", new object?[] { DateTime.MinValue, new DateTime(1899, 12, 31) }));

        contents.Sheets["Tickets"][1].ShouldBe(new object?[] { });
    }

    [Fact]
    public void Sheet_names_are_made_valid_and_unique()
    {
        var contents = Roundtrip(
            Sheet("Report: Q1/Q2 [draft]?"), Sheet("report: q1/q2 [draft]?"), Sheet(""), Sheet(new string('n', 50)));

        contents.SheetNames.Count.ShouldBe(4);
        contents.SheetNames.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(4);
        contents.SheetNames.ShouldAllBe(name => name.Length >= 1 && name.Length <= 31 && name.IndexOfAny("[]:*?/\\".ToCharArray()) < 0);
    }

    [Fact]
    public void Columns_past_Z_are_named_like_excel_names_them()
    {
        var header = Enumerable.Range(0, 28).Select(i => $"H{i}").ToArray();
        var sheet = new XlsxSheet("Wide", header);
        sheet.Rows.Add(header.Select(h => (object?)h).ToArray());

        var xml = Roundtrip(sheet).Parts["xl/worksheets/sheet1.xml"];

        xml.ShouldContain("r=\"AA1\"");
        xml.ShouldContain("r=\"AB2\"");
        xml.ShouldContain("<autoFilter ref=\"A1:AB2\"/>");
    }

    [Fact]
    public void An_empty_sheet_still_opens_and_a_workbook_needs_a_sheet()
    {
        Roundtrip(new XlsxSheet("Empty", new[] { "Only a header" })).Sheets["Empty"].Count.ShouldBe(1);

        Should.Throw<ArgumentException>(() => XlsxWriter.Write(Array.Empty<XlsxSheet>()));
    }
}
