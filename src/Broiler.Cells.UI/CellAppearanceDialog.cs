using Broiler.Graphics.Color;
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
    private readonly StandardLabel _selection;
    private readonly StandardButton[] _buttons;

    private CellAppearanceDialog(UiWindow owner, BSize viewport, string description,
        Func<string, BColor> initialColor, Action<string, BColor> apply, StandardDialog dialog)
    {
        _selection = new StandardLabel { Text = description };
        _buttons =
        [
            Button("Text color…", () => ChooseColor("Text color", "text")),
            Button("Fill color…", () => ChooseColor("Fill color", "fill")),
            Button("Outer borders…", () => ChooseColor("Outer border color", "outer")),
            Button("Inner borders…", () => ChooseColor("Inner border color", "inner")),
            Button("All borders…", () => ChooseColor("Border color", "all")),
            Button("No borders", () => Apply("none", default)),
            Button("Automatic text", () => Apply("text", default)),
            Button("No fill", () => Apply("fill", default)),
            Button("Close", () => dialog.Cancel())
        ];
        AddChild(_selection);
        foreach (var button in _buttons)
            AddChild(button);

        void Apply(string action, BColor color)
        {
            apply(action, color);
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
        Func<string, BColor> initialColor, Action<string, BColor> apply)
    {
        var dialog = new StandardDialog
        {
            Title = "Cell Appearance",
            PreferredSize = new BSize(440, 185),
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

    protected override BSize MeasureCore(BSize availableSize)
    {
        _selection.Measure(new BSize(availableSize.Width, 24));
        foreach (var button in _buttons)
            button.Measure(new BSize(ColumnWidth(availableSize.Width), 28));
        return new BSize(availableSize.Width, 122);
    }

    protected override void ArrangeCore(BRect bounds)
    {
        _selection.Arrange(new BRect(bounds.Left, bounds.Top, bounds.Width, 24));
        double width = ColumnWidth(bounds.Width);
        double height = Math.Clamp((bounds.Height - 38) / 3, 0, 28);
        for (int i = 0; i < _buttons.Length; i++)
        {
            double left = bounds.Left + i % 3 * (width + 6);
            double top = bounds.Top + 26 + i / 3 * (height + 6);
            _buttons[i].Arrange(new BRect(left, top, width, height));
        }
    }

    private static double ColumnWidth(double width) => Math.Max(0, (width - 12) / 3);
}
