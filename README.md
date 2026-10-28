# Viteka Utilidades · Centro de accesos 0.12.1

Aplicación de escritorio Windows: **Angular + WebView2**, con un anfitrión .NET para preferencias, controles de ventana y actualizaciones. La interfaz se incluye en el paquete. La autoridad de usuarios y el acceso Oracle se ejecutan en el servicio del servidor de la farmacia.

## Accesos y administración

Cuentas y permisos centralizados en el servicio Windows de la farmacia, con HTTPS y confianza privada fijada en el cliente. Usuario: Inicio y aplicaciones autorizadas; titular: todas las aplicaciones y gestión de usuarios normales; Viteka: superadministración y configuración técnica. Configuración agrupa Mi cuenta (filas con ventanas de edición), Usuarios para titulares/Viteka y conexión técnica solo para Viteka. Contraseñas nuevas desde 3 caracteres.

Actualizaciones tiene una pestaña propia: búsqueda cada 24 horas mientras la app está abierta o al siguiente arranque, botón manual, aviso e historial de versiones publicadas y ejecuciones locales. Solo titular/Viteka pueden iniciar el instalador y deben hacerlo en el servidor. Ver [usuarios y accesos](docs/accesos.md).

## Incluido

- Diseño editorial del HTML Viteka Portal Estudio 2026 aplicado a toda la interfaz: marfil, verde petróleo, titulares y superficies de producto.
- Inicio con portal de soporte, AnyDesk local, manuales, formación, web y contactos.
- Menú lateral plegable, logotipo inferior sin eslogan y apariencia clara/oscura/automática persistente.
- Isotipo bicolor transparente original en ventana, ejecutable e instalador.
- Utilidades, Favoritos y Actividad conservan estados vacíos, sin acceso a datos de NixFarma.
- Preferencias locales por usuario, conservadas entre versiones.
- Canal de pruebas GitHub: búsqueda, descarga verificada y apertura del instalador con confirmación/UAC.
- Instalador x64 autocontenido para .NET, con bootstrapper de WebView2 verificado como Microsoft. Si falta el runtime, su instalación inicial puede requerir internet.

La configuración Oracle y la prueba fija de solo lectura se ejecutan ahora en el servicio del servidor, bajo LocalService y con DSN de sistema de 64 bits. Las credenciales previas se migran desde el perfil original al entrar Viteka en el propio servidor. Todavía no hay consultas de negocio de utilidades. Ver [conexión a NixFarma](docs/conexion-nixfarma.md).

[Descargar desde GitHub Releases](https://github.com/RafaViteka/Viteka_Utilidades/releases). Instalar sobre la versión anterior sin desinstalar. Desde 0.6 se puede actualizar desde Configuración; desde 0.5 hay que incorporar el actualizador mediante una descarga manual una vez. Paquetes de prueba sin firma digital del editor, no certificados para distribuir a farmacias.

## Desarrollo

Node 24.19.0, npm 11.9.0 y .NET SDK 10.0.403. Dependencias fijadas en frontend/package-lock.json y packages.lock.json.

En cloud, desde `/workspace/Viteka_Utilidades`:

```bash
bash scripts/setup-cloud.sh
python scripts/smoke-ui.py
```

El primer comando instala/compila Angular, copia el resultado a `src/Viteka.Desktop/Web`, compila .NET y ejecuta las 55 comprobaciones y el smoke HTTP. El segundo requiere `pip install -r requirements-ui.txt` y Chromium/Chrome instalado; no descarga navegadores. Genera una captura en artifacts/ui.

Para editar la interfaz en navegador:

```bash
cd frontend
npm ci
npm start
```

En navegador las funciones nativas de actualización están deshabilitadas; las preferencias de vista previa usan almacenamiento local independiente.

Para ejecutar la aplicación en Windows, primero compilar/copy Angular y después `dotnet run --project src/Viteka.Desktop -c Release`. Para generar instalador:

```powershell
./scripts/publish-windows.ps1 -BuildInstaller
```

Requiere Node/npm, Python, SDK .NET e Inno Setup 6 en el equipo de desarrollo. Esos componentes no se exigen al usuario final.

## Validación

El workflow manual windows-preview.yml compila/ejecuta pruebas del núcleo y servidor, pruebas de interfaz en Chrome, prueba nativa WebView2 (arranque, navegación, guardado por puente y reapertura), y una descarga real de actualización verificada, sin ejecutar ese instalador. Revisar el resultado del run antes de afirmar que pasaron en Windows. La revisión gráfica y el ciclo interactivo de instalación/UAC requieren el equipo del usuario.

[Alcance del MVP](docs/mvp.md) · [Arquitectura](docs/estructura.md) · [Distribución](docs/distribucion.md)

[Centro de accesos y recursos autorizados](docs/portal.md). El HTML adjunto se utiliza como referencia de diseño y contenidos: no se ejecutan sus scripts de instalación, protocolo ni feed remoto.

## Ventana, iconos y zoom (0.9)

La ventana se ajusta al área útil de su monitor y respeta la barra de tareas al abrir y maximizar, con DPI por monitor. Se asignan explícitamente iconos Win32 grandes/pequeños y una identidad estable Viteka.Utilidades, compartida con el acceso directo del instalador. Se incluye un icono físico transparente para el shell. Un acceso antiguo ya anclado puede conservar la caché anterior: desanclar y volver a anclar desde el acceso actualizado si ocurre.

Ctrl + rueda aumenta/reduce el contenido; la rueda normal desplaza la página. La barra nativa muestra porcentaje y botones − / 100 % / + fuera del área escalada. Ctrl + 0 vuelve al 100 %. Rango 50–200 %, guardado por usuario. Se actualiza desde Configuración sin desinstalar. Las pruebas Windows comprueban iconos Win32, área útil y rueda de ratón con Ctrl mediante entrada real de prueba.
