# MVP Angular + WebView2

## Alcance confirmado

El usuario aprobó Angular dentro de WebView2 para acercar la aplicación al HTML elegido. El anfitrión .NET mantiene únicamente las funciones nativas. No se añaden aplicaciones ni vínculos a NixFarma hasta revisar el MVP.

La navegación, iconos, estados vacíos, preferencias y actualizaciones se implementan en componentes Angular. Los archivos se empaquetan localmente bajo Web. WebView2 usa una carpeta de usuario bajo LocalAppData para su caché: no escribe en la carpeta compartida del ejecutable. No hace falta una conexión ni un servidor para abrir la interfaz una vez disponible el runtime.

## Puente nativo

Mensajes correlacionados con identificadores UUID. Solo se admiten app.state, settings.start, update.check, update.install y update.publication. Angular no elige rutas de disco, SQL ni URL de instaladores. El anfitrión acepta mensajes únicamente del origen virtual local confiable, bloquea navegación externa, ventanas nuevas, permisos y descargas del motor. La actualización requiere además confirmación nativa y UAC.

Los mensajes de progreso no contienen credenciales ni datos de farmacia. La vista en navegador permite revisar la interfaz y preferencias de prueba, pero no ejecutar acciones nativas.

## Pruebas del MVP

- Build de producción Angular con revisión estricta de TypeScript/plantillas.
- CSP sin scripts en línea, estilos locales; inlining de CSS crítico desactivado para evitar handlers de carga bloqueados.
- Navegación y estados vacíos, anchura lateral/iconos, persistencia de preferencia tras recarga, ausencia de errores JavaScript y comportamiento con puente simulado.
- Smoke Windows del WebView2 real: versión recibida desde .NET, cinco secciones, cambio de preferencia guardado por .NET y selección de Favoritos tras recargar. Usa estado temporal aislado.
- Descarga de actualización real desde GitHub con tamaño y SHA-256 verificados, sin ejecutar el instalador en el test.

No confundir pruebas automáticas con aprobación gráfica del usuario ni con certificación de todos los Windows 10/11 y Server 2022/2025. Revisar los resultados de la ejecución concreta.

## Entrega

Mismo AppId del instalador: instalar encima preserva preferencias. Se incluye un bootstrapper oficial de WebView2 y se verifica su firma Microsoft durante empaquetado; el instalador de Viteka aún no tiene firma del editor. Si ya existe el runtime, se reutiliza; si falta, puede necesitar internet para instalarlo. El usuario decide cuándo aplicar cada actualización. Cada entrega incrementa la versión base X.Y.Z.

## Después del MVP

Recoger la revisión de la interfaz en el equipo del usuario. Después definir la primera utilidad, contratos y consultas, y validar el acceso Oracle/ODBC real en la red de farmacia. El backend previo se conserva sin ampliar su alcance.
