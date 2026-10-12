using System.Data;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using W = DocumentFormat.OpenXml.Wordprocessing;
using S = DocumentFormat.OpenXml.Spreadsheet;
namespace LaundryPractice;

public static class ReportExporter
{
    public static string SectionName(string entity) => entity switch { "users" => "Пользователи", "services" => "Услуги", _ => "Заказы" };
    public static string Header(string column) => column switch
    {
        "id" => "№",
        "login" => "Логин",
        "full_name" => "ФИО",
        "email" => "Почта",
        "role" => "Роль",
        "active" => "Активна",
        "client" => "Клиент",
        "service" => "Услуга",
        "branch" => "Филиал",
        "received_date" => "Дата приёма",
        "due_date" => "Срок готовности",
        "quantity" => "Количество",
        "unit_price" => "Цена, руб.",
        "total" => "Сумма, руб.",
        "status" => "Статус",
        "description" => "Описание",
        "name" => "Название",
        "unit" => "Единица",
        "price" => "Цена, руб.",
        _ => column
    };
    public static string CellText(object? value) => value switch { null or DBNull => "", DateTime d => d.ToString("dd.MM.yyyy"), decimal d => d.ToString("0.###"), bool b => b ? "Да" : "Нет", _ => value.ToString() ?? "" };
    public static void Export(DataTable rows, string title, string path, string format)
    {
        if (format == "docx") ExportDocx(rows, title, path);
        else if (format == "xlsx") ExportXlsx(rows, title, path);
        else if (format == "pdf") ExportPdf(rows, title, path);
        else throw new ArgumentException("Неизвестный формат файла.");
    }
    private static W.Paragraph Paragraph(string text, bool bold = false, int size = 18) => new(new W.Run(new W.RunProperties(new W.RunFonts { Ascii = "Open Sans", HighAnsi = "Open Sans" }, new W.Bold { Val = bold }, new W.FontSize { Val = size.ToString() }), new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    private static double ColumnWeight(string column) => column switch { "id" => 0.45, "description" => 2.2, "branch"=>1.3,"quantity" or "due_date"=>1.35,"received_date"=>1.15,"name" or "client" or "service" or "full_name" or "email" => 1.65, _ => 1.05 };
    private static W.TableRow DocxRow(IEnumerable<string> values, int[] widths, bool header = false)
    {
        var row = new W.TableRow();
        if (header) row.Append(new W.TableRowProperties(new W.TableHeader()));
        int column = 0;
        foreach (string value in values)
        {
            var cell = new W.TableCell();
            cell.Append(new W.TableCellProperties(new W.TableCellWidth { Type = W.TableWidthUnitValues.Dxa, Width = widths[column++].ToString() }, new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = header ? "EAEBED" : "FFFFFF" }));
            cell.Append(Paragraph(value, header)); row.Append(cell);
        }
        return row;
    }
    public static void ExportDocx(DataTable rows, string title, string path)
    {
        using (var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var part = document.AddMainDocumentPart(); part.Document = new W.Document(); var body = new W.Body(); part.Document.Append(body);
            body.Append(Paragraph($"Сеть химчисток и прачечных — {title}", true, 28));
            body.Append(Paragraph($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}. Записей: {rows.Rows.Count}."));
            var table = new W.Table(new W.TableProperties(new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct }, new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Size = 4 }, new W.LeftBorder { Val = W.BorderValues.Single, Size = 4 },
                new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 }, new W.RightBorder { Val = W.BorderValues.Single, Size = 4 },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 }, new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4 }), new W.TableLayout { Type = W.TableLayoutValues.Fixed }));
            double[] weights = rows.Columns.Cast<DataColumn>().Select(c => ColumnWeight(c.ColumnName)).ToArray();
            int[] widths = weights.Select(w => (int)(15398 * w / weights.Sum())).ToArray();
            table.Append(new W.TableGrid(widths.Select(w => new W.GridColumn { Width = w.ToString() })));
            table.Append(DocxRow(rows.Columns.Cast<DataColumn>().Select(c => Header(c.ColumnName)), widths, true));
            foreach (DataRow row in rows.Rows) table.Append(DocxRow(row.ItemArray.Select(CellText), widths));
            body.Append(table);
            body.Append(new W.SectionProperties(new W.PageSize { Width = 16838, Height = 11906, Orient = W.PageOrientationValues.Landscape }, new W.PageMargin { Top = 720, Bottom = 720, Left = 720, Right = 720, Header = 300, Footer = 300, Gutter = 0 }));
            part.Document.Save();
        }
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update);
        var entry = archive.GetEntry("[Content_Types].xml")!;
        System.Xml.Linq.XDocument types;
        using (var stream = entry.Open()) types = System.Xml.Linq.XDocument.Load(stream);
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        var defaultXml = types.Root!.Elements(ns + "Default").FirstOrDefault(e => (string?)e.Attribute("Extension") == "xml");
        defaultXml?.SetAttributeValue("ContentType", "application/xml");
        if (!types.Root.Elements(ns + "Override").Any(e => (string?)e.Attribute("PartName") == "/word/document.xml"))
            types.Root.Add(new System.Xml.Linq.XElement(ns + "Override", new System.Xml.Linq.XAttribute("PartName", "/word/document.xml"), new System.Xml.Linq.XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")));
        entry.Delete(); using (var stream = archive.CreateEntry("[Content_Types].xml").Open()) types.Save(stream);
    }
    private static string ColumnName(int index)
    {
        string name = ""; for (int n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name; return name;
    }
    public static void ExportXlsx(DataTable rows, string title, string path)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart(); workbook.Workbook = new S.Workbook();
        var style = workbook.AddNewPart<WorkbookStylesPart>();
        style.Stylesheet = new S.Stylesheet(
            new S.NumberingFormats(new S.NumberingFormat { NumberFormatId = 164, FormatCode = "dd.mm.yyyy" }) { Count = 1 },
            new S.Fonts(new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Open Sans" }), new S.Font(new S.Bold(), new S.FontSize { Val = 11 }, new S.FontName { Val = "Open Sans" })) { Count = 2 },
            new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }), new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })) { Count = 2 },
            new S.Borders(new S.Border()) { Count = 1 }, new S.CellStyleFormats(new S.CellFormat()) { Count = 1 },
            new S.CellFormats(new S.CellFormat(), new S.CellFormat { FontId = 1, ApplyFont = true }, new S.CellFormat { NumberFormatId = 164, ApplyNumberFormat = true }, new S.CellFormat { NumberFormatId = 4, ApplyNumberFormat = true }) { Count = 4 },
            new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
        var worksheet = workbook.AddNewPart<WorksheetPart>();
        var data = new S.SheetData(); var columns = new S.Columns();
        for (int c = 0; c < rows.Columns.Count; c++) columns.Append(new S.Column { Min = (uint)c + 1, Max = (uint)c + 1, Width = rows.Columns[c].ColumnName == "description" ? 40 : 22, CustomWidth = true });
        worksheet.Worksheet = new S.Worksheet(new S.SheetViews(new S.SheetView(new S.Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = S.PaneValues.BottomLeft, State = S.PaneStateValues.Frozen }) { WorkbookViewId = 0 }), columns, data);
        var head = new S.Row { RowIndex = 1 };
        for (int c = 0; c < rows.Columns.Count; c++) head.Append(new S.Cell { CellReference = ColumnName(c) + "1", DataType = S.CellValues.InlineString, StyleIndex = 1, InlineString = new S.InlineString(new S.Text(Header(rows.Columns[c].ColumnName))) });
        data.Append(head);
        uint index = 2;
        foreach (DataRow row in rows.Rows)
        {
            var line = new S.Row { RowIndex = index };
            for (int c = 0; c < rows.Columns.Count; c++)
            {
                object value = row[c]; var cell = new S.Cell { CellReference = ColumnName(c) + index };
                if (value is DateTime date) { cell.DataType = S.CellValues.Number; cell.StyleIndex = 2; cell.CellValue = new S.CellValue(date.ToOADate().ToString(CultureInfo.InvariantCulture)); }
                else if (value is decimal number) { cell.DataType = S.CellValues.Number; cell.StyleIndex = rows.Columns[c].ColumnName == "quantity" ? 0u : 3u; cell.CellValue = new S.CellValue(number.ToString(CultureInfo.InvariantCulture)); }
                else if (value is int or long) { cell.DataType = S.CellValues.Number; cell.CellValue = new S.CellValue(Convert.ToString(value, CultureInfo.InvariantCulture)!); }
                else { cell.DataType = S.CellValues.InlineString; cell.InlineString = new S.InlineString(new S.Text(CellText(value)) { Space = SpaceProcessingModeValues.Preserve }); }
                line.Append(cell);
            }
            data.Append(line); index++;
        }
        worksheet.Worksheet.Append(new S.AutoFilter { Reference = $"A1:{ColumnName(rows.Columns.Count - 1)}{index - 1}" });
        workbook.Workbook.Append(new S.Sheets(new S.Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = title[..Math.Min(31, title.Length)] }));
        workbook.Workbook.Save(); worksheet.Worksheet.Save(); style.Stylesheet.Save();
    }
    private static List<string> Wrap(string text, XGraphics graphics, XFont font, double width)
    {
        var lines = new List<string>(); string current = "";
        foreach (string paragraph in text.Replace("\r", "").Split('\n'))
        {
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (graphics.MeasureString(candidate, font).Width <= width) { current = candidate; continue; }
                if (current.Length > 0) { lines.Add(current); current = ""; }
                foreach (char ch in word)
                {
                    if (current.Length > 0 && graphics.MeasureString(current + ch, font).Width > width) { lines.Add(current); current = ""; }
                    current += ch;
                }
            }
            lines.Add(current); current = "";
        }
        return lines;
    }
    public static void ExportPdf(DataTable rows, string title, string path)
    {
        if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new OpenSansResolver();
        using var document = new PdfDocument(); document.Info.Title = title;
        var font = new XFont("Open Sans", 8); var heading = new XFont("Open Sans", 15);
        XGraphics? graphics = null; double y = 0; double[] widths = new double[rows.Columns.Count];
        double[] weights = rows.Columns.Cast<DataColumn>().Select(c => ColumnWeight(c.ColumnName)).ToArray();
        void NewPage()
        {
            graphics?.Dispose(); var page = document.AddPage(); page.Size = PdfSharp.PageSize.A4; page.Orientation = PdfSharp.PageOrientation.Landscape;
            graphics = XGraphics.FromPdfPage(page);
            graphics.DrawString($"Химчистки и прачечные — {title}", heading, XBrushes.Black, new XRect(24, 20, page.Width.Point - 48, 25), XStringFormats.TopLeft);
            graphics.DrawString($"{DateTime.Now:dd.MM.yyyy HH:mm} · записей: {rows.Rows.Count} · страница {document.PageCount}", font, XBrushes.Black, new XRect(24, 49, page.Width.Point - 48, 18), XStringFormats.TopLeft);
            double x = 24, total = weights.Sum(); for (int c = 0; c < widths.Length; c++) widths[c] = (page.Width.Point - 48) * weights[c] / total;
            y = 74;
            for (int c = 0; c < widths.Length; c++)
            {
                graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(234, 235, 237)), x, y, widths[c], 36);
                var lines = Wrap(Header(rows.Columns[c].ColumnName), graphics, font, widths[c] - 8);
                for (int l = 0; l < lines.Count; l++) graphics.DrawString(lines[l], font, XBrushes.Black, new XRect(x + 4, y + 5 + l * 11, widths[c] - 8, 11), XStringFormats.TopLeft);
                x += widths[c];
            }
            y += 36;
        }
        NewPage();
        foreach (DataRow row in rows.Rows)
        {
            List<string>[] lines = row.ItemArray.Select((value, c) => Wrap(CellText(value), graphics!, font, widths[c] - 8)).ToArray();
            double height = Math.Max(26, lines.Max(l => l.Count) * 11 + 10);
            if (y + height > 550) NewPage();
            double x = 24;
            for (int c = 0; c < widths.Length; c++)
            {
                graphics!.DrawRectangle(new XPen(XColor.FromArgb(145, 151, 163), 0.4), x, y, widths[c], height);
                for (int l = 0; l < lines[c].Count; l++) graphics.DrawString(lines[c][l], font, XBrushes.Black, new XRect(x + 4, y + 5 + l * 11, widths[c] - 8, 11), XStringFormats.TopLeft);
                x += widths[c];
            }
            y += height;
        }
        graphics!.Dispose(); document.Save(path);
    }
}

public sealed class OpenSansResolver : IFontResolver
{
    public byte[] GetFont(string faceName) => System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "res", "OpenSans-Regular.ttf"));
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new("OpenSans", isBold, isItalic);
}
