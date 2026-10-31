using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;

// Runs only inside an installer-smoke fixture, alongside its installed client.
// Invoke the actual packaged handoff, retaining the same read guard as DownloadAsync.
if (args is not [var installer, var report]) return 2;
var temporary = Path.GetFullPath(Path.GetTempPath());
var client = Path.GetFullPath(AppContext.BaseDirectory);
if (!client.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) ||
    !Path.GetRelativePath(temporary, client).StartsWith("VitekaPcReviewSmoke-", StringComparison.Ordinal))
    throw new InvalidOperationException("Handoff check requires an owned temporary installer fixture.");
var stage = "installer-guard";
try
{
    using var guard = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
    var hash = Convert.ToHexString(await SHA256.HashDataAsync(guard)).ToLowerInvariant();
    stage = "product-metadata";
    var metadata = FileVersionInfo.GetVersionInfo(installer);
    if (metadata.ProductName?.TrimEnd(' ', '\0') != "Viteka Utilidades - Pruebas") throw new InvalidDataException("Wrong fixture product.");
    stage = "prepare-handoff";
    var assembly = Assembly.LoadFrom(Path.Combine(client, "Viteka.Desktop.dll"));
    var method = assembly.GetType("Viteka.Desktop.UpdateCompanion", throwOnError: true)!
        .GetMethod("PrepareAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
    var log = Path.Combine(Path.GetDirectoryName(installer)!, "instalacion.log");
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var task = (Task<string>)method.Invoke(null, [installer, hash, guard.Length, log, "test", timeout.Token])!;
    var manifest = await task;
    File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, stage = "handoff-ready", manifest }));
    return 0;
}
catch (Exception error)
{
    var monitorText = "";
    try
    {
        var expected = Path.Combine(Path.GetDirectoryName(installer)!, "Monitor", "Viteka.Desktop.exe");
        foreach (var process in Process.GetProcessesByName("Viteka.Desktop"))
        {
            using (process)
            {
                if (!string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase) || process.MainWindowHandle == 0) continue;
                var window = AutomationElement.FromHandle(process.MainWindowHandle);
                monitorText = string.Join(" | ", window.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                    .Cast<AutomationElement>().Select(x => x.Current.Name).Where(x => x.Length > 0).Distinct().Take(20));
            }
        }
    }
    catch (Exception) { /* Diagnostic only, confined to this owned fixture. */ }
    File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, stage, error = error.ToString(), monitorText }));
    Console.Error.WriteLine("Handoff fixture failure: " + stage + ": " + error);
    return 1;
}
