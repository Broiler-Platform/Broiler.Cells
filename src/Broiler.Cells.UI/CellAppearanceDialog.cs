using Broiler.Graphics.Color;
using Broiler.Documents.Model;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ColorDialog.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Window;

namespace Broiler.Cells.UI;

internal sealed class CellAppearanceDialog : UiElement
{
    private readonly UiElement[][] _rows;
    private readonly StandardLabel _borderHeading = new() { Text = "Cell borders" };
    private int _width = 1;
    private TableBorderStyle _style = TableBorderStyle.Solid;

    private CellAppearanceDialog(UiWindow owner, BSize viewport, string description,
        Func<string, BColor> initialColor, Action<string, BColor, double, TableBorderStyle> apply, StandardDialog dialog)
    {
        _rows =
        [
            [new StandardLabel { Text = description }],
            [new StandardLabel { Text = "Text and fill" }],
            [Button("Text color…", () => ChooseColor("Text color", "text")),
             Button("Fill color…", () => ChooseColor("Fill color", "fill")),
             Button("Automatic text", () => Apply("text", default)),
             Button("No fill", () => Apply("fill", default))],
            [_borderHeading],
            [new StandardLabel { Text = "Width" }, .. Choices(new[] { 1, 2, 3 }, _width, value => _width = value)],
            [new StandardLabel { Text = "Line style" }, .. Choices(new[] { TableBorderStyle.Solid,
                TableBorderStyle.Dotted, TableBorderStyle.Dashed }, _style, value => _style = value)],
            [Button("Outer borders…", () => ChooseColor("Outer border color", "outer")),
             Button("Inner borders…", () => ChooseColor("Inner border color", "inner")),
             Button("All borders…", () => ChooseColor("Border color", "all")),
             Button("No borders", () => Apply("none", default))],
            [Button("Close", () => dialog.Cancel())]
        ];
        foreach (var row in _rows)
            foreach (var element in row)
                AddChild(element);

        void Apply(string action, BColor color)
        {
            apply(action, color, _width, _style);
            dialog.Accept();
        }

        void ChooseColor(string title, string action)
        {
            BColor color = initialColor(action);
            var chooser = new StandardColorDialog
            {
                Title = title,
                CanClose = !OperatingSystem.IsBrowser(),
                SelectedColor = color.A > 0 ? color : BColor.Black
            };
            chooser.ResultCompleted += (_, e) =>
            {
                if (e.Result.Kind != UiDialogResultKind.Accepted)
                    return;

                BColor selectedColor = chooser.SelectedColor;
                void ApplySelection()
                {
                    if (dialog.IsPresented)
                        Apply(action, selectedColor);
                }

                // Let the native chooser finish closing before closing its owner window.
                if (chooser.IsBrokenOut && dialog.Session is { } session)
                    session.Dispatcher.Post(ApplySelection);
                else
                    ApplySelection();
            };

            // Keep the chooser inside the workbook viewport when the host draws both windows.
            // With a separate native window, make that window the chooser's owner.
            UiWindow chooserOwner = ReferenceEquals(dialog.Session, owner.Session) ? owner : dialog;
            BSize preferred = chooser.PreferredSize;
            BRect placement;
            if (chooserOwner.Session is { Host: IUiWindowHost } nativeSession)
            {
                BSize client = nativeSession.Host.ViewportSize;
                placement = new BRect((client.Width - preferred.Width) / 2,
                    (client.Height - preferred.Height) / 2, preferred.Width, preferred.Height);
            }
            else
            {
                placement = StandardDialogPlacement.Center(viewport, preferred);
            }
            _ = chooser.ShowColorModal(chooserOwner, placement);
        }
    }

    public static void Show(UiWindow owner, BSize viewport, string description,
        Func<string, BColor> initialColor, Action<string, BColor, double, TableBorderStyle> apply)
    {
        var dialog = new StandardDialog
        {
            Title = "Cell Appearance",
            PreferredSize = new BSize(520, 320),
            CanClose = !OperatingSystem.IsBrowser()
        };
        dialog.AddChild(new CellAppearanceDialog(owner, viewport, description, initialColor, apply, dialog));
        _ = dialog.ShowModal(owner, StandardDialogPlacement.Center(viewport, dialog.PreferredSize));
    }

    private static StandardButton Button(string text, Action action)
    {
        var button = new StandardButton { Text = text, AccessibleName = text };
        button.Clicked += (_, _) => action();
        return button;
    }

    private StandardButton[] Choices<T>(T[] values, T selected, Action<T> choose) where T : notnull
    {
        var buttons = new StandardButton[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            T value = values[i];
            buttons[i] = Button(value.ToString()!, () =>
            {
                selected = value;
                choose(value);
                UpdateSelection();
                Invalidate(UiInvalidationKind.Render);
            });
        }
        UpdateSelection();
        return buttons;

        void UpdateSelection()
        {
            for (int i = 0; i < values.Length; i++)
            {
                string label = values[i].ToString()!;
                bool active = selected.Equals(values[i]);
                buttons[i].Text = active ? $"✓ {label}" : label;
                buttons[i].AccessibleName = active ? $"{label} (selected)" : label;
            }
        }
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        foreach (var row in _rows)
            foreach (var element in row)
                element.Measure(new BSize(ColumnWidth(availableSize.Width, row.Length), 28));
        return new BSize(availableSize.Width, 244);
    }

    protected override void ArrangeCore(BRect bounds)
    {
        const double gap = 5;
        double height = Math.Clamp((bounds.Height - (_rows.Length - 1) * gap) / _rows.Length, 0, 28);
        for (int r = 0; r < _rows.Length; r++)
        {
            var row = _rows[r];
            bool footer = r == _rows.Length - 1;
            double width = ColumnWidth(bounds.Width, footer ? 4 : row.Length);
            double left = footer ? bounds.Right - width : bounds.Left;
            for (int c = 0; c < row.Length; c++)
                row[c].Arrange(new BRect(left + c * (width + 6), bounds.Top + r * (height + gap), width, height));
        }
    }

    protected override void RenderCore(UiRenderContext context)
    {
        base.RenderCore(context);
        var row = _borderHeading.Bounds;
        double length = Math.Min(120, Math.Max(0, row.Width / 2 - 8));
        double segment = _style switch
        {
            TableBorderStyle.Dotted => _width,
            TableBorderStyle.Dashed => 3 * _width,
            _ => length
        };
        for (double offset = 0; offset < length; offset += segment + _width)
            context.RenderList.FillRect(new BRect(row.Right - length + offset,
                row.Y + (row.Height - _width) / 2, Math.Min(segment, length - offset), _width), CellsPalette.Title);
    }

    private static double ColumnWidth(double width, int columns) => Math.Max(0, (width - (columns - 1) * 6) / columns);
}
