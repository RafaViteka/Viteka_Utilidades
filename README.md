# Viteka Utilidades · MVP Angular 0.7.0

Aplicación de escritorio Windows: **Angular + WebView2**, con un anfitrión .NET para preferencias, controles de ventana y actualizaciones. La interfaz se incluye en el paquete, sin web alojada ni servidor requerido para abrir el MVP.

## Incluido

- Identidad Viteka y diseño B: isotipo junto a Utilidades, logotipo lateral, verdes, lima y marfil.
- Inicio, Utilidades, Favoritos, Actividad y Configuración, con estados vacíos.
- Preferencias locales por usuario, conservadas entre versiones.
- Canal de pruebas GitHub: búsqueda, descarga verificada y apertura del instalador con confirmación/UAC.
- Instalador x64 autocontenido para .NET, con bootstrapper de WebView2 verificado como Microsoft. Si falta el runtime, su instalación inicial puede requerir internet.

No hay utilidades operativas ni conexión a NixFarma. Se revisará primero el MVP visual. No se añaden ahora vínculos a la base de datos.

[Descargar desde GitHub Releases](https://github.com/RafaViteka/Viteka_Utilidades/releases). Instalar sobre la versión anterior sin desinstalar. Desde 0.6 se puede actualizar desde Configuración; desde 0.5 hay que incorporar el actualizador mediante una descarga manual una vez. Paquetes de prueba sin firma digital del editor, no certificados para distribuir a farmacias.

## Desarrollo

Node 24.19.0, npm 11.9.0 y .NET SDK 10.0.403. Dependencias fijadas en frontend/package-lock.json y packages.lock.json.

En cloud, desde `/workspace/Viteka_Utilidades`:

```bash
bash scripts/setup-cloud.sh
python scripts/smoke-ui.py
```

El primer comando instala/compila Angular, copia el resultado a `src/Viteka.Desktop/Web`, compila .NET y ejecuta las 27 comprobaciones y el smoke HTTP. El segundo requiere `pip install -r requirements-ui.txt` y Chromium/Chrome instalado; no descarga navegadores. Genera una captura en artifacts/ui.

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
