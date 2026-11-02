using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace Helpdesk.Export;

/// <summary>One worksheet: a bold header row followed by data rows. A cell is a string, a number, a UTC time or null.</summary>
public sealed class XlsxSheet
{
    public XlsxSheet(string name, IReadOnlyList<string> header)
    {
        Name = name;
        Header = header;
    }

    public string Name { get; }

    public IReadOnlyList<string> Header { get; }

    public List<object?[]> Rows { get; } = new();
}

/// <summary>
/// Writes a minimal .xlsx (a zip of a few XML files) without a library: frozen bold header, filter, column widths.
/// Text is always written as a string cell, never as a formula, so a title such as =HYPERLINK(...) stays text in Excel.
/// </summary>
public static class XlsxWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Excel refuses more than 32,767 characters in a cell and more than 31 in a sheet name
    private const int MaxCellLength = 32767;
    private const int MaxSheetName = 31;

    private const int StyleHeader = 1;
    private const int StyleDate = 2;

    public static byte[] Write(IReadOnlyList<XlsxSheet> sheets)
    {
        if (sheets.Count == 0)
        {
            throw new ArgumentException("A workbook needs at least one sheet.", nameof(sheets));
        }

        var names = UniqueNames(sheets.Select(sheet => sheet.Name).ToList());

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>");
            Add(zip, "xl/workbook.xml", Workbook(names));
            Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));
            Add(zip, "xl/styles.xml", Styles);
            for (var i = 0; i < sheets.Count; i++)
            {
                Add(zip, $"xl/worksheets/sheet{i + 1}.xml", Worksheet(sheets[i]));
            }
        }

        return output.ToArray();
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string ContentTypes(int sheets)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
            + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (var i = 1; i <= sheets; i++)
        {
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        return sb.Append("</Types>").ToString();
    }

    private static string Workbook(IReadOnlyList<string> names)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        for (var i = 0; i < names.Count; i++)
        {
            sb.Append($"<sheet name=\"{Escape(names[i])}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        }

        return sb.Append("</sheets></workbook>").ToString();
    }

    private static string WorkbookRels(int sheets)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (var i = 1; i <= sheets; i++)
        {
            sb.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
        }

        sb.Append($"<Relationship Id=\"rId{sheets + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    // style 0 = default, 1 = bold header, 2 = date and time
    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
        + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\\ hh:mm\"/></numFmts>"
        + "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
        + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
        + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
        + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
        + "<cellXfs count=\"3\">"
        + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
        + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
        + "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "</cellXfs>"
        + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
        + "</styleSheet>";

    private static string Worksheet(XlsxSheet sheet)
    {
        var columns = sheet.Header.Count;
        var widths = new int[columns];
        for (var c = 0; c < columns; c++)
        {
            widths[c] = sheet.Header[c].Length;
        }

        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>"
            + "<sheetFormatPr defaultRowHeight=\"15\"/>");

        var body = new StringBuilder("<sheetData>");
        body.Append("<row r=\"1\">");
        for (var c = 0; c < columns; c++)
        {
            body.Append(TextCell(c, 1, sheet.Header[c], StyleHeader));
        }

        body.Append("</row>");

        var rowNumber = 1;
        foreach (var row in sheet.Rows)
        {
            rowNumber++;
            body.Append($"<row r=\"{rowNumber}\">");
            for (var c = 0; c < Math.Min(columns, row.Length); c++)
            {
                body.Append(Cell(c, rowNumber, row[c], out var width));
                widths[c] = Math.Max(widths[c], width);
            }

            body.Append("</row>");
        }

        body.Append("</sheetData>");

        sb.Append("<cols>");
        for (var c = 0; c < columns; c++)
        {
            // the widest value wins, within reason, so one long description does not stretch the sheet
            var width = Math.Clamp(widths[c] + 2, 8, 60);
            sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width}\" customWidth=\"1\"/>");
        }

        sb.Append("</cols>").Append(body);
        if (columns > 0)
        {
            sb.Append($"<autoFilter ref=\"A1:{ColumnName(columns - 1)}{Math.Max(rowNumber, 1)}\"/>");
        }

        return sb.Append("</worksheet>").ToString();
    }

    private static string Cell(int column, int row, object? value, out int width)
    {
        switch (value)
        {
            case null:
                width = 0;
                return string.Empty;
            case DateTime time when time.Year < 1900:
                // Excel has no dates before 1900 (and OADate throws); a placeholder such as 0001-01-01 is an empty cell
                width = 0;
                return string.Empty;
            case DateTime time:
                width = 16;
                return $"<c r=\"{ColumnName(column)}{row}\" s=\"{StyleDate}\"><v>{time.ToOADate().ToString("R", CultureInfo.InvariantCulture)}</v></c>";
            case int or long or short or byte or double or float or decimal:
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                width = number.ToString(CultureInfo.InvariantCulture).Length;
                return $"<c r=\"{ColumnName(column)}{row}\"><v>{number.ToString("R", CultureInfo.InvariantCulture)}</v></c>";
            default:
                var text = Clean(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                width = text.Length;
                return TextCell(column, row, text, 0);
        }
    }

    private static string TextCell(int column, int row, string text, int style)
    {
        var styleAttribute = style == 0 ? string.Empty : $" s=\"{style}\"";
        return $"<c r=\"{ColumnName(column)}{row}\" t=\"inlineStr\"{styleAttribute}><is><t xml:space=\"preserve\">{Escape(Clean(text))}</t></is></c>";
    }

    /// <summary>Drops characters XML cannot carry (most control characters) and cuts to Excel's cell limit.</summary>
    private static string Clean(string text)
    {
        var sb = new StringBuilder(Math.Min(text.Length, MaxCellLength));
        for (var i = 0; i < text.Length && sb.Length < MaxCellLength; i++)
        {
            var c = text[i];
            if (c is '\t' or '\n' or '\r' || (c >= 0x20 && c != 0xFFFE && c != 0xFFFF && !char.IsSurrogate(c)))
            {
                sb.Append(c);
            }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) && sb.Length < MaxCellLength - 1)
            {
                sb.Append(c).Append(text[++i]);
            }
        }

        return sb.ToString();
    }

    private static string Escape(string text) => System.Security.SecurityElement.Escape(text) ?? string.Empty;

    /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
    internal static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    /// <summary>Sheet names must be unique, at most 31 characters and free of []:*?/\ .</summary>
    private static List<string> UniqueNames(IReadOnlyList<string> wanted)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in wanted)
        {
            var name = new string(raw.Select(c => "[]:*?/\\".IndexOf(c) >= 0 ? ' ' : c).ToArray()).Trim();
            if (name.Length == 0)
            {
                name = "Sheet";
            }

            name = name.Length > MaxSheetName ? name[..MaxSheetName] : name;
            var candidate = name;
            for (var n = 2; !used.Add(candidate); n++)
            {
                var suffix = $" {n}";
                candidate = name[..Math.Min(name.Length, MaxSheetName - suffix.Length)] + suffix;
            }

            result.Add(candidate);
        }

        return result;
    }
}
