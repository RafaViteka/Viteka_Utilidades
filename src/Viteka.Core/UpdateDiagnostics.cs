using System.ComponentModel;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Viteka.Core;

// Safe support evidence only. Never persist exception messages, stack traces,
// paths, URLs, credentials or a serialized session.
public sealed record UpdateFailure(DateTimeOffset At, string Phase, string Category, string Code);
public sealed class UpdateOperationException(UpdateFailure failure) : IOException(UpdateDiagnostics.Describe(failure))
{
    public UpdateFailure Failure { get; } = failure;
}
public static class UpdateDiagnostics
{
    private static readonly HashSet<string> Phases = ["preparing", "downloading", "verifying", "handoff", "installing"];
    public static UpdateFailure Capture(Exception exception, string phase)
    {
        if (exception is UpdateOperationException known) return known.Failure;
        phase = Phases.Contains(phase) ? phase : "preparing";
        var category = exception switch
        {
            UnauthorizedAccessException => "PERMISSION",
            Win32Exception { NativeErrorCode: 1223 } => "CANCELLED",
            Win32Exception => "WINDOWS",
            HttpRequestException => "NETWORK",
            CryptographicException => "CRYPTO",
            InvalidDataException => "INTEGRITY",
            IOException => "FILES",
            _ => "STATE"
        };
        var number = exception is Win32Exception windows ? windows.NativeErrorCode : exception.HResult;
        return new(DateTimeOffset.UtcNow, phase, category, $"VU-UPD-{phase.ToUpperInvariant()}-{category}-{number:X8}");
    }
    public static string Describe(UpdateFailure failure)
    {
        var action = failure.Phase switch
        {
            "downloading" => "descargar el paquete",
            "verifying" => "verificar el paquete",
            "handoff" => "preparar el instalador y el permiso de Windows",
            "installing" => "instalar la actualización",
            _ => "preparar la actualización"
        };
        var help = failure.Category switch
        {
            "PERMISSION" => "Windows ha denegado el acceso a un archivo o recurso necesario.",
            "CANCELLED" => "Se canceló el permiso de Windows.",
            "NETWORK" => "No se ha podido completar la comunicación necesaria.",
            "INTEGRITY" => "El paquete no ha superado la comprobación de integridad o de producto.",
            "FILES" => "No se ha podido leer, copiar o guardar un archivo necesario.",
            _ => "La operación no ha podido completarse."
        };
        return $"No se pudo {action}. {help} Código de diagnóstico: {failure.Code}.";
    }
    public static void Save(string directory, UpdateFailure failure)
    {
        try
        {
            var updates = Path.Combine(directory, "Updates");
            Directory.CreateDirectory(updates);
            var temporary = Path.Combine(updates, $".error-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(failure));
                File.Move(temporary, Path.Combine(updates, "last-error.json"), true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* The public code remains visible even when the local folder is not writable. */ }
    }
}
