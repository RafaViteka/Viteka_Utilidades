using System.Data.Odbc;
using System.IO;
using System.Text.RegularExpressions;
using Viteka.Core;

namespace Viteka.Server;

internal sealed record NixFarmaProbeResult(bool Success, string Code, string Message, string? SqlState = null, int? OracleCode = null);

internal sealed class NixFarmaConnectionTester
{
    private int running;
    internal bool IsRunning => Volatile.Read(ref running) != 0;
    internal async Task<NixFarmaProbeResult> TestAsync(OracleProfile settings, CancellationToken ct)
    {
        settings.Validate();
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            return new(false, "busy", "El controlador sigue atendiendo la prueba anterior. Espera o cierra y vuelve a abrir Viteka.");
        var operation = Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows()) return new NixFarmaProbeResult(false, "driver", "Se requiere Windows.");
            try
            {
                var builder = new OdbcConnectionStringBuilder { Dsn = settings.Dsn };
                builder["UID"] = settings.User;
                builder["PWD"] = settings.Password;
                using var connection = new OdbcConnection(builder.ConnectionString);
                NixFarmaReadProbe.Execute(connection);
                return new NixFarmaProbeResult(true, "verified", "Conexión verificada: la consulta de prueba en modo de solo lectura ha respondido correctamente.");
            }
            catch (OdbcException error) { return Classify(error); }
            catch (InvalidDataException) { return new(false, "unexpected_result", "La base de datos no devolvió el resultado esperado de la prueba."); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or DllNotFoundException or BadImageFormatException or System.ComponentModel.Win32Exception or NotSupportedException or System.Security.SecurityException or IOException)
            { return new(false, "driver", "No se pudo utilizar el controlador ODBC de 64 bits. Revisa su instalación en este servidor."); }
            finally { Interlocked.Exchange(ref running, 0); }
        });
        try { return await operation.WaitAsync(TimeSpan.FromSeconds(30), ct); }
        catch (TimeoutException) { return new(false, "timeout", "El controlador no respondió en 30 segundos. La aplicación sigue disponible; no se iniciará otra prueba mientras continúe la anterior."); }
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static NixFarmaProbeResult Classify(OdbcException error)
    {
        // Only structured/numeric codes leave the native layer. Never return the raw
        // driver message: it can contain connection details or credentials.
        var errors = error.Errors.Cast<OdbcError>().ToArray();
        var state = errors.Select(e => e.SQLState).FirstOrDefault(s => Regex.IsMatch(s ?? "", "^[A-Z0-9]{5}$"));
        var oracle = errors.Select(e =>
        {
            var match = Regex.Match(e.Message, @"ORA-(\d{5})", RegexOptions.CultureInvariant);
            return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
        }).FirstOrDefault(n => n is not null);
        if (oracle is null) oracle = errors.Select(e => Math.Abs((long)e.NativeError)).Where(n => n > 0 && n <= 99999).Select(n => (int?)n).FirstOrDefault();
        var (code, message) = (state, oracle) switch
        {
            ("IM002", _) => ("dsn_missing", "No se encuentra este DSN de sistema de 64 bits. Comprueba NIXFARMA en el administrador ODBC de 64 bits de este equipo."),
            ("IM003" or "IM014", _) => ("driver", "No se pudo cargar el controlador Oracle de 64 bits. Revisa la arquitectura y la instalación del cliente Oracle."),
            (_, 1017) or ("28000", null) => ("authentication", "Oracle ha rechazado el usuario o la contraseña. Revisa las credenciales con el administrador de NixFarma."),
            (_, 28000) => ("account_locked", "La cuenta Oracle está bloqueada. Debe revisarla el administrador de NixFarma."),
            (_, 28001) => ("password_expired", "La contraseña Oracle ha caducado. Debe actualizarla el administrador de NixFarma."),
            (_, 12154) => ("tns", "Oracle no puede resolver el servicio TNS. Revisa la configuración del cliente Oracle para este usuario de Windows."),
            (_, 12514 or 12541 or 12545 or 12170) or ("08001" or "08S01", _) => ("network", "No se alcanza el servicio Oracle. Revisa el servicio TNS, el servidor y su disponibilidad en la red."),
            (_, 1031) => ("permissions", "La cuenta no tiene los permisos necesarios para la operación de lectura. Solicita una cuenta de consulta con permisos mínimos."),
            (_, 942) => ("table_access", "La cuenta Oracle no puede acceder a una tabla necesaria. Revisa el esquema y los permisos de consulta con Viteka."),
            (_, 904) => ("column", "Oracle no reconoce una columna de la consulta. Viteka debe revisar los nombres de las tablas y sus campos."),
            (_, 1453) => ("read_only", "Oracle no permitió iniciar la transacción de solo lectura. No se ha ejecutado la consulta."),
            ("HYT00" or "HYT01", _) => ("timeout", "La conexión o consulta ha agotado su tiempo de espera."),
            _ => ("oracle", "Oracle no pudo completar la operación de lectura. Revisa el diagnóstico con Viteka.")
        };
        return new(false, code, message, state, oracle);
    }
}
