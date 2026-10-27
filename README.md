# Viteka Utilidades

Base de aplicación de escritorio para Windows, con identidad Viteka y catálogo modular de herramientas para farmacia.

## Estado actual

- Cliente WPF: navegación, búsqueda, favoritos persistentes, actividad local y comprobación del servicio local.
- Servidor ASP.NET Core: catálogo, estado y operaciones protegidos por autorización de puesto; consultas bloqueadas mientras Oracle no esté configurado.
- Vinculación de usuario/equipo mediante código de un solo uso, revocación y caducidad de credenciales.
- Comprobación manual de versiones en GitHub Releases, con revisión de la publicación; sin descarga/instalación automática.
- Contratos compartidos, sin dependencias de interfaz ni Oracle.
- Prototipo HTML autónomo en `prototipo/index.html`.

**No conecta a Oracle, no ejecuta utilidades reales y no es una versión desplegable en farmacia.** El host es una demostración local; el instalador todavía no registra automáticamente el servicio ni habilita TLS o acceso LAN. Se ha añadido soporte de servicio y administración manual, pendiente de prueba en Windows. La vinculación está implementada en modo local; no identifica a cada empleado ni define permisos por utilidad.

## Desarrollo en este entorno cloud

Desde `/workspace/Viteka_Utilidades`:

```bash
bash scripts/setup-cloud.sh
```

Instala o reutiliza el SDK .NET 10.0.403 en `/workspace/.dotnet`, restaura con los lockfiles, compila la solución, ejecuta veintiuna comprobaciones de persistencia y autorización y comprobaciones HTTP reales sobre el servidor. El test arranca y detiene su propio proceso y necesita libre el puerto 5187.

Arranque manual del servidor de demostración:

```bash
/workspace/.dotnet/dotnet src/Viteka.Server/bin/Release/net10.0/Viteka.Server.dll
```

El proceso escucha exclusivamente en loopback, puerto 5187. `/health` debe devolver `status: ok` y `mode: demo`; `/api/status` devuelve HTTP 401 sin autorización. Tras vincular el cliente debe indicar `databaseConfigured: false`. Estos resultados no validan Oracle. No publicar enlaces de preview para este servidor.

## Desarrollo en Windows

Requiere SDK .NET 10.0.403 (no necesario en cada puesto para un futuro paquete autocontenido). Desde la raíz del repositorio, en dos terminales:

```powershell
dotnet restore Viteka.Utilidades.slnx --locked-mode
dotnet build Viteka.Utilidades.slnx -c Release --no-restore
dotnet run --project src/Viteka.Server -c Release --no-build
```

```powershell
dotnet run --project src/Viteka.Desktop -c Release --no-build
```

El cliente utiliza un catálogo local de demostración; la comprobación en Configuración consulta el estado real del servidor local. Favoritos, preferencia de inicio y las últimas 100 acciones locales se guardan por usuario en `%LOCALAPPDATA%\Viteka\Utilidades`. No se guardan credenciales ni datos de farmacia. Si un archivo está dañado o tiene un esquema incompatible, se conserva y se muestra un aviso; no se sobrescribe automáticamente. En esta versión, iniciar el servidor solo en el servidor de farmacia no permitirá conectarse desde otros puestos: el acceso LAN está pendiente de implementar.

## Validación y siguientes pasos

Comprobado en Linux: compilación completa, veintiuna comprobaciones de persistencia y autorización y smoke test HTTP del servidor. Pendiente: ejecución visual WPF en Windows, compatibilidad de versiones/ediciones concretas de Windows 10/11 y Server 2022/2025, acceso real ODBC, cuenta Oracle de lectura, ejecución del flujo de vinculación y DPAPI en Windows, TLS y empaquetado compartido.

[Arquitectura y decisiones](docs/estructura.md). Las secciones históricas se precisan mediante las actualizaciones posteriores: la dirección vigente es un servicio central con ODBC y clientes sin Oracle.

## Vinculación local de demostración

[Procedimiento administrativo y límites](docs/vinculacion.md). Los códigos no deben enviarse al chat ni guardarse en el repositorio. El smoke test usa su propio directorio temporal y no deja autorizaciones en el registro real.

## Ejecutables e instalación

Se publica para Windows x64 de forma autocontenida. [Empaquetado y plan de actualización online](docs/distribucion.md). El instalador preparado es una vista previa local; no es el instalador de producción para los puestos de la farmacia.

[Preparación del servicio Windows](docs/servicio-windows.md).
