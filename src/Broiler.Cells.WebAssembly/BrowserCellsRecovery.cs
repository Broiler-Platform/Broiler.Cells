using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broiler.Cells.Core;
using Broiler.UI.Edit.Standard;

namespace Broiler.Cells.WebAssembly;

internal static partial class BrowserCellsApp
{
    private static StandardEdit? _observedEditor;
    private static string? _observedEditorText;

    private static void ObservePendingEditor()
    {
        var editor = _app?.Session.FocusedElement as StandardEdit;
        if (editor is not null && ReferenceEquals(editor, _observedEditor) && editor.Text != _observedEditorText) {
            _revision++; _modified = true;
        }
        _observedEditor = editor; _observedEditorText = editor?.Text;
    }

    internal static string RecoverySnapshot()
    {
        _dispatcher?.Drain(); ObservePendingEditor();
        if (_app is null) return "";
        var rows = JsonSerializer.Deserialize(SheetSnapshot(), SheetJsonContext.Default.StringArrayArray)!;
        // Pending editor text is part of recovery, without committing or stealing focus.
        if (_app.Session.FocusedElement is StandardEdit edit && _app.CellControl.SelectedCell is { } cell)
            rows[cell.Row][cell.Column] = edit.Text;
        ValidateRecoveryRows(rows);
        return JsonSerializer.Serialize(new CellsRecoverySnapshot {
            name = _name, payload = JsonSerializer.Serialize(rows, SheetJsonContext.Default.StringArrayArray),
            generation = _generation, revision = _revision,
        }, CellsRecoveryJsonContext.Default.CellsRecoverySnapshot);
    }

    private static void ValidateRecoveryRows(string[][] rows)
    {
        if (rows.Length is < 1 or > 100 || rows[0] is null || rows[0].Length is < 1 or > 100)
            throw new InvalidDataException("Invalid recovery sheet dimensions.");
        int columns = rows[0].Length; long characters = 0;
        foreach (var row in rows) {
            if (row is null || row.Length != columns) throw new InvalidDataException("Recovery rows must have equal dimensions.");
            foreach (var value in row) {
                if (value is null || value.Length > MaximumCellCharacters) throw new InvalidDataException("Recovery cell exceeds browser limits.");
                characters += value.Length;
                if (characters > 250000) throw new InvalidDataException("Recovery text exceeds browser limits.");
            }
        }
    }

    internal static bool RestoreRecovery(string name, string payload)
    {
        _dispatcher?.Drain();
        try {
            if (_app is null) return false;
            if (payload.Length > 2 * MaximumBytes) throw new InvalidDataException("Recovery payload exceeds browser limits.");
            var rows = JsonSerializer.Deserialize(payload, SheetJsonContext.Default.StringArrayArray) ?? throw new InvalidDataException("Empty recovery payload.");
            ValidateRecoveryRows(rows);
            var sheet = new SheetModel(rows.Length, rows[0].Length, (r,c) => rows[r][c]);
            _app.CellControl.SetSheet(sheet); AttachSheet(sheet);
            _app.CellControl.SelectCell(new CellPosition(0,0)); _app.CellEditLine.CancelEdit(); _app.CellControl.Grid.Focus();
            _name = Path.GetFileName(name.Replace('\\','/')); _generation++; _revision++; _modified = true;
            PublishDocumentState(); BrowserInterop.ReportOperation("Recovered", "Local values restored as modified content. Export a separate backup."); return true;
        } catch (Exception error) when (error is not OutOfMemoryException) {
            BrowserInterop.ReportOperation("Failed", "Recovery failed; current sheet retained. " + error.Message); return false;
        }
    }
}

internal sealed class CellsRecoverySnapshot
{
    public string name { get; set; } = "";
    public string format { get; set; } = "cells-json";
    public string payload { get; set; } = "";
    public string notes { get; set; } = "";
    public long generation { get; set; }
    public long revision { get; set; }
}
[JsonSerializable(typeof(CellsRecoverySnapshot))]
internal partial class CellsRecoveryJsonContext : JsonSerializerContext { }
