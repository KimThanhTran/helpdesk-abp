using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Helpdesk.Export;

/// <summary>A tiny reader for the .xlsx files this project writes, so tests can look at what a person would open.</summary>
public sealed class XlsxContents
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";

    public List<string> SheetNames { get; } = new();

    /// <summary>Per sheet: rows of cells, each a string, a double (numbers and dates) or null.</summary>
    public Dictionary<string, List<List<object?>>> Sheets { get; } = new();

    /// <summary>Every part's raw XML, so a test can check there is no formula anywhere.</summary>
    public Dictionary<string, string> Parts { get; } = new();

    public static XlsxContents Read(byte[] bytes)
    {
        var result = new XlsxContents();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            result.Parts[entry.FullName] = reader.ReadToEnd();
        }

        var workbook = XDocument.Parse(result.Parts["xl/workbook.xml"]);
        var rels = XDocument.Parse(result.Parts["xl/_rels/workbook.xml.rels"]).Root!.Elements(Pkg + "Relationship")
            .ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);

        foreach (var sheet in workbook.Root!.Element(Main + "sheets")!.Elements(Main + "sheet"))
        {
            var name = (string)sheet.Attribute("name")!;
            result.SheetNames.Add(name);
            var path = "xl/" + rels[(string)sheet.Attribute(Rel + "id")!];
            result.Sheets[name] = Rows(XDocument.Parse(result.Parts[path]));
        }

        return result;
    }

    private static List<List<object?>> Rows(XDocument sheet) =>
        sheet.Root!.Element(Main + "sheetData")!.Elements(Main + "row").Select(row =>
        {
            var cells = new List<object?>();
            foreach (var cell in row.Elements(Main + "c"))
            {
                var column = ColumnIndex((string)cell.Attribute("r")!);
                while (cells.Count < column)
                {
                    cells.Add(null);
                }

                cells.Add((string?)cell.Attribute("t") == "inlineStr"
                    ? string.Concat(cell.Element(Main + "is")!.Elements(Main + "t").Select(t => t.Value))
                    : double.Parse((string)cell.Element(Main + "v")!, System.Globalization.CultureInfo.InvariantCulture));
            }

            return cells;
        }).ToList();

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter))
        {
            index = index * 26 + (c - 'A' + 1);
        }

        return index - 1;
    }
}
