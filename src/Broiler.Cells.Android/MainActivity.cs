using System;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Broiler.Cells.Core;
using Broiler.Cells.UI;
using Broiler.Hosting.Android;

namespace Broiler.Cells.Android;

[Activity(
    Label = "Broiler Cells",
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
        ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode |
        ConfigChanges.Density,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class MainActivity : Activity
{
    private const int OpenDocumentRequest = 1001;
    private const int CreateDocumentRequest = 1002;
    private AndroidBroilerView? _view;
    private CellsUiHost? _host;
    private CellsApp? _app;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetSoftInputMode(SoftInput.AdjustResize);

        Broiler.Graphics.Text.BSystemFonts.InstallFontFileResolver();
        Broiler.Graphics.Imaging.BImageCodecs.Use(new Broiler.Media.MediaCodecCatalog(
            Broiler.Media.Image.Managed.ManagedImageCodecs.CreateCodecs()));

        _view = new AndroidBroilerView(this, CellsPalette.Canvas);
        _host = new CellsUiHost(
            () => _view.ViewportSize,
            () => _view.Scale,
            _view.InvalidateFrame,
            _view.Present,
            _view.GetClipboardText,
            _view.SetClipboardText,
            _view.NotifyCaretChanged,
            getRenderer: () => _view.Renderer);

        _app = new CellsApp(
            _host,
            Finish,
            compactMode: true,
            openUrl: url => StartActivity(new global::Android.Content.Intent(
                global::Android.Content.Intent.ActionView,
                global::Android.Net.Uri.Parse(url.AbsoluteUri))));

        _view.RenderFrame = _app.RenderFrame;
        _view.DispatchInput = input => _app.Dispatch(input);
        _view.GetSession = () => _app.Session;

        var content = new AndroidInsetLayout(this);
        content.AddView(_view, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));
        SetContentView(content);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _view?.SetResumed(true);
    }

    protected override void OnPause()
    {
        _view?.SetResumed(false);
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _view?.Dispose();
        _app?.Dispose();
        _host?.Dispose();
        _app = null;
        _host = null;
        _view = null;
        base.OnDestroy();
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is not null && _view?.ProcessKeyEvent(e) == true)
            return true;
        return base.DispatchKeyEvent(e);
    }

    public override void OnBackPressed() => Finish();
}
