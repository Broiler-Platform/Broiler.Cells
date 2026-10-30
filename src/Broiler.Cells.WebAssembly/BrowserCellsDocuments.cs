using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broiler.Cells.Core;
using Broiler.Documents;
using Broiler.Documents.Csv;

namespace Broiler.Cells.WebAssembly;

internal static partial class BrowserCellsApp
{
    internal const int MaximumBytes = 2 * 1024 * 1024;
    internal const int MaximumCellCharacters = 16384;
    private static bool _modified;
    private static long _generation, _revision;
    private static string _name = "Spreadsheet.csv";
    private static SheetModel? _observedSheet;
    private static WorkbookModel? _observedWorkbook;

    private static void AttachSheet(SheetModel sheet)
    {
        if (_observedWorkbook is not null) _observedWorkbook.Changed -= SheetChanged;
        if (_observedSheet is not null) _observedSheet.Changed -= SheetChanged;
        _observedSheet = sheet;
        _observedWorkbook = sheet.Workbook;
        if (_observedWorkbook is not null) _observedWorkbook.Changed += SheetChanged;
        else sheet.Changed += SheetChanged;
    }

    private static void SheetChanged(object? sender, EventArgs args)
    {
        _modified = true;
        _revision++;
        if (_observedWorkbook?.Sheets.Count > 1 && _name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            _name = Path.ChangeExtension(_name, ".xlsx");
    }

    private static bool CommitPendingEdits()
    {
        _app?.CellControl.CommitEdit();
        if (_app?.CellControl.Grid.LastInputError is { } gridError) { BrowserInterop.ReportOperation("Failed", gridError); return false; }
        _app?.CellEditLine.CommitEdit();
        if (_app?.CellControl.Grid.LastInputError is { } lineError) { BrowserInterop.ReportOperation("Failed", lineError); return false; }
        _app?.CellControl.Grid.Focus();
        return true;
    }

    private static void FileAction(string action)
    {
        if (!CommitPendingEdits()) return;
        PublishDocumentState();
        BrowserInterop.RequestAction(action, _modified, _name);
    }

    internal static void CompleteAction(string action) => _dispatcher?.Post(() => {
        if (_app is null) return;
        if (action is "new" or "close") {
            var sheet = new SheetModel();
            _app.View.SetDocument(sheet); AttachSheet(sheet);
            _app.CellControl.SelectCell(new CellPosition(0, 0));
            _app.CellEditLine.CancelEdit(); _app.CellControl.Grid.Focus();
            _generation++; _revision++; _modified = false; _name = "Spreadsheet.csv";
            BrowserInterop.ReportOperation("New", "New sheet. Local recovery is available; export a separate backup.");
        } else if (action == "open") BrowserInterop.RequestOpenFile(".csv,.xlsx,.ods");
        PublishDocumentState();
    });

    internal static void LoadDocument(string fileName, string base64Data) => _dispatcher?.Post(() => {
        try {
            if (_app is null) return;
            if (base64Data.Length > ((MaximumBytes + 2) / 3) * 4) throw new InvalidDataException("Spreadsheet exceeds the 2 MiB file limit.");

            byte[] bytes = Convert.FromBase64String(base64Data);
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("Spreadsheet exceeds the 2 MiB file limit.");
            using var input = new MemoryStream(bytes, writable: false);
            var options = new CsvReadOptions(limits: new DocumentLimits(maxDocumentBytes: MaximumBytes, maxRunLength: MaximumCellCharacters, maxParagraphCount: 10001));
            SheetModel sheet = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                ? SheetCsvSerializer.Load(input, options, rejectDataLoss: true)
                : SheetDocumentSerializer.Load(input, fileName, new DocumentReadOptions(limits: options.Limits));
            ValidateSheet(sheet);
            // Install only after parsing and all limits succeed.
            _app.View.SetDocument(sheet); AttachSheet(sheet);
            _app.CellControl.SelectCell(new CellPosition(0, 0)); _app.CellEditLine.CancelEdit(); _app.CellControl.Grid.Focus();
            _name = Path.GetFileName(fileName.Replace('\\', '/')); _generation++; _revision++; _modified = false;
            BrowserInterop.ReportOperation("Opened", $"Opened {_name}: {sheet.RowCount} rows × {sheet.ColumnCount} columns. Typed values and basic number formats are supported; supported same-worksheet formulas calculate automatically. {sheet.ImportNotice}");
        } catch (Exception error) when (error is not OutOfMemoryException) {
            BrowserInterop.ReportOperation("Failed", "Open failed; current sheet retained. " + error.Message);
        }
        PublishDocumentState();
    });

    private static void ValidateSheet(SheetModel sheet)
    {
        long characters = 0;
        for (int r = 0; r < sheet.RowCount; r++) for (int c = 0; c < sheet.ColumnCount; c++) {
            string value = sheet[r,c]; characters += value.Length;
            if (value.Length > MaximumCellCharacters || characters > 250000)
                throw new InvalidDataException("Spreadsheet exceeds the 16,384 characters per cell / 250,000 total character limit.");
        }
    }

    internal static string PrepareExport(string name)
    {
        _dispatcher?.Drain(); if (!CommitPendingEdits()) return string.Empty;
        try {
            if (_app is null) return string.Empty;
            name = Path.GetFileName(name.Replace('\\', '/')).Trim();
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            if (Path.GetExtension(name).Length == 0) name += ".csv";

            foreach (var worksheet in _app.View.Workbook.Sheets) ValidateSheet(worksheet.Sheet);
            using var output = new MemoryStream();
            SheetDocumentSerializer.Save(_app.CellControl.Sheet, output, name);
            if (output.Length > MaximumBytes) throw new InvalidDataException("Spreadsheet export exceeds the 2 MiB limit.");
            _name = name; PublishDocumentState();
            return name + "\0" + Convert.ToBase64String(output.ToArray());
        } catch (Exception error) when (error is not OutOfMemoryException) {
            BrowserInterop.ReportOperation("Failed", "Export failed; sheet retained. " + error.Message); return string.Empty;
        }
    }

    internal static void ExportCompleted(string outcome, string message) => _dispatcher?.Post(() => {
        // Downloads cannot confirm that bytes reached disk. Never clear modified state here.
        BrowserInterop.ReportOperation(outcome, outcome == "ExportInitiated" ? "Download requested. Disk persistence is not confirmed; modified sheet stays open." : message);
        PublishDocumentState();
    });

    private static void PublishDocumentState()
    {
        BrowserInterop.SetTitle("Broiler Cells — " + _name + (_modified ? " *" : ""));
        BrowserInterop.DocumentState(_name, _modified, _generation, _revision);
    }

    internal static string SelectedCellText()
    {
        _dispatcher?.Drain(); if (!CommitPendingEdits()) return string.Empty;
        return _app?.CellControl.SelectedCell is { } cell ? _app.CellControl.Sheet[cell.Row, cell.Column] : string.Empty;
    }

    internal static string SheetSnapshot()
    {
        _dispatcher?.Drain();
        if (_app is null) return "[]";
        var sheet = _app.CellControl.Sheet;
        var rows = new string[sheet.RowCount][];
        for (int r=0; r<sheet.RowCount; r++) { rows[r] = new string[sheet.ColumnCount]; for (int c=0; c<sheet.ColumnCount; c++) rows[r][c]=sheet[r,c]; }
        return JsonSerializer.Serialize(rows, SheetJsonContext.Default.StringArrayArray);
    }
}

[JsonSerializable(typeof(string[][]))]
internal partial class SheetJsonContext : JsonSerializerContext { }
