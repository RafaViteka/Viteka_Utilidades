# Viteka Utilidades · MVP visual 0.6.0

Aplicación Windows para reunir futuras utilidades de farmacia. **Primero se revisará el MVP visual; las aplicaciones y vínculos a NixFarma se desarrollarán después.**

## Interfaz actual

- Diseño B con identidad Viteka: isotipo en la barra superior, logotipo en el lateral, verde oscuro, selección lima y fondos marfil.
- Inicio, Utilidades, Favoritos, Actividad y Configuración con estilos propios de WPF.
- Catálogo y actividad vacíos, sin ejemplos de stock, caducidades o compras.
- Preferencia local de abrir Favoritos al iniciar.
- Canal de pruebas de GitHub: búsqueda, descarga verificada y apertura del instalador con aprobación. No se instala nada sin confirmación.
- Los formularios de conexión/vinculación están retirados de la interfaz. No hace falta iniciar un servidor para revisar el MVP.

[Descargas de prueba en GitHub Releases](https://github.com/RafaViteka/Viteka_Utilidades/releases). Descargar el instalador o extraer **todo** el ZIP y abrir `Cliente/Viteka.Desktop.exe`. Para incorporar el actualizador hay que instalar manualmente la 0.6.0 una vez sobre la anterior, sin desinstalarla. Las versiones posteriores podrán descargarse e instalarse desde Configuración. Paquetes sin firma digital, todavía no listos para farmacia.

## Desarrollo

En este entorno Linux, desde `/workspace/Viteka_Utilidades`:

```bash
bash scripts/setup-cloud.sh
```

Reutiliza/instala el SDK .NET 10.0.403, restaura con lockfiles, compila la solución, ejecuta 27 comprobaciones del núcleo y el smoke HTTP. Este último usa un registro temporal aislado y necesita libre el puerto 5187.

WPF se compila en Linux, pero se ejecuta en Windows. Con el SDK indicado, desde la raíz del proyecto en Windows:

```powershell
dotnet restore Viteka.Utilidades.slnx --locked-mode
dotnet build Viteka.Utilidades.slnx -c Release --no-restore
dotnet run --project src/Viteka.Desktop -c Release --no-build
```

La referencia HTML autónoma, también vacía, está en `prototipo/index.html`.

## Empaquetado

```powershell
./scripts/publish-windows.ps1 -BuildInstaller
```

Requiere Windows e Inno Setup 6. Publica cliente/servidor autocontenidos x64. El workflow manual `windows-preview.yml` ejecuta las pruebas, compila el instalador y publica una prerelease en GitHub. La versión se obtiene de `Directory.Build.props`.

## Backend conservado, desarrollo aplazado

El servidor previo y sus pruebas se conservan, pero no se amplían para esta revisión. No hay conexión real a Oracle ni utilidades operativas. El catálogo autenticado devuelve una lista vacía. La API mantiene autorización de puestos y el host está restringido a loopback. No exponerlo a la red ni cambiar el DSN de NixFarma.

Preferencias locales: `%LOCALAPPDATA%\Viteka\Utilidades`. No se borran archivos anteriores; los favoritos/historial de utilidades retiradas no reaparecen en el MVP. No guardar datos de pacientes ni contraseñas en el repositorio o disco compartido.

[Decisiones y arquitectura](docs/estructura.md) · [Distribución](docs/distribucion.md) · [Servicio Windows pendiente de validar](docs/servicio-windows.md)

La compilación y las pruebas automáticas no sustituyen la revisión gráfica ni la prueba de instalación en el equipo del usuario.
