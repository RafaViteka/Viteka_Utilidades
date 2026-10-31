using System.Windows;
using Microsoft.Web.WebView2.Core;
namespace Viteka.Desktop;

public partial class App : Application
{
    internal static bool SmokeTest { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        if(e.Args is ["--watch-update",var manifest]) { StartupUri=null; UpdateCompanion.Run(manifest); return; }
        SmokeTest = e.Args.Contains("--smoke-ui");
        if (e.Args.Contains("--check-webview-runtime"))
        {
            try { CoreWebView2Environment.GetAvailableBrowserVersionString(); Shutdown(0); }
            catch (WebView2RuntimeNotFoundException) { Shutdown(2); }
            return;
        }
        if (SmokeTest) DispatcherUnhandledException += (_, args) => { WriteSmokeFailure(args.Exception, "dispatcher"); args.Handled = true; Shutdown(1); };
        try { WindowsShellIntegration.RegisterIdentity(); }
        catch (Exception exception) { if (SmokeTest) { WriteSmokeFailure(exception, "shell-startup"); Shutdown(1); return; } throw; }
        base.OnStartup(e);
    }
    private static void WriteSmokeFailure(Exception exception, string stage)
    {
        var report = Environment.GetEnvironmentVariable("VITEKA_UI_SMOKE_REPORT");
        if (!string.IsNullOrWhiteSpace(report)) System.IO.File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { passed = false, stage, error = exception.ToString() }));
    }
}
