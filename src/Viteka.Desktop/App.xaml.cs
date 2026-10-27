using System.Windows;
using Microsoft.Web.WebView2.Core;
namespace Viteka.Desktop;
public partial class App : Application
{
    internal static bool SmokeTest { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        SmokeTest = e.Args.Contains("--smoke-ui");
        if (e.Args.Contains("--check-webview-runtime"))
        {
            try { CoreWebView2Environment.GetAvailableBrowserVersionString(); Shutdown(0); }
            catch (WebView2RuntimeNotFoundException) { Shutdown(2); }
            return;
        }
        base.OnStartup(e);
    }
}
