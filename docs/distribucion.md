# Instalación y actualizaciones de Viteka Utilidades

## Entrega prevista

Un instalador `.exe` para Windows x64 con aplicación autocontenida: no exigir instalar .NET en cada puesto. El servidor albergará el servicio y la aplicación compartida. Los clientes abrirán un lanzador desde un acceso directo a la carpeta compartida. El controlador Oracle solo se necesita en el servidor.

## Empaquetado implementado: vista previa local

- `scripts/publish-windows.ps1`: restaura dependencias bloqueadas, compila, comprueba el núcleo y publica cliente/servidor autocontenidos para Windows x64.
- `packaging/windows/Viteka.Utilidades.iss`: instalador Inno Setup 6, accesos directos y desinstalación. AppId independiente para no confundir esta vista previa con una futura instalación de producción.
- `.github/workflows/windows-preview.yml`: ejecución manual en Windows, compilación del instalador, comprobación HTTP y artefacto descargable. No publica versiones ni despliega a farmacias. Requiere que Inno Setup 6 esté disponible en el runner.

En Windows, con SDK .NET 10.0.403 e Inno Setup 6:

```powershell
./scripts/publish-windows.ps1 -BuildInstaller
```

Salida esperada: `artifacts/installer/VitekaUtilidades-Setup-0.4.0-win-x64-preview.exe`. Sin `-BuildInstaller` se generan carpetas con los ejecutables y sus dependencias en `artifacts/windows-x64`. No copiar únicamente los `.exe`: esta publicación autocontenida incluye archivos auxiliares necesarios.

La vista previa se instala para evaluación local. No instala un servicio de Windows, no abre puertos, no habilita acceso LAN ni actualizaciones automáticas. El host demo debe arrancarse desde su acceso directo. Preferencias y credenciales permanecen fuera de la instalación y no se eliminan al desinstalar.

Validado en cloud: publicación autocontenida de cliente/servidor win-x64 y formato de los ejecutables. Pendientes: ejecutar los binarios y compilar/probar el instalador en Windows. El workflow todavía no se ha ejecutado. Los binarios no están firmados digitalmente.

## Actualización online: diseño propuesto, no implementado

El mecanismo comprobará nuevas versiones por HTTPS desde un destino configurable. Política confirmada: avisar y aplicar solo cuando el administrador lo autorice. Canal elegido: GitHub Releases. No se requiere acceso remoto interactivo al escritorio de la farmacia para descargar una actualización.

1. Comprobar un manifiesto firmado que indique versión, compatibilidad de API/estado y ubicación del paquete. Verificar firma con clave pública de confianza incluida en el actualizador. No confiar en una clave recibida junto al paquete sin una cadena de confianza previa.
2. Descargar por HTTPS y comprobar firma del manifiesto, hash del paquete y firma Authenticode del instalador/editor permitido. Un hash por sí solo no acredita el origen. Rechazar versiones inesperadas, incompatibles o degradaciones no autorizadas.
3. Preparar una carpeta de versión nueva, sin sobrescribir ejecutables en uso. No ejecutar el paquete directamente desde una descarga sin verificar.
4. Comprobar puestos activos y programar el cambio con el administrador. No fiarse solo de que no haya procesos en el servidor: los clientes pueden ejecutar el archivo compartido.
5. Actualizar el servicio y cambiar el destino del lanzador de forma controlada. No mezclar clientes y servidores con API incompatible. Evitar que un usuario ordinario pueda reemplazar el paquete o apuntar el servicio a otro ejecutable.
6. Verificar salud/autorización y conservar la versión anterior. La vuelta atrás debe contemplar compatibilidad del estado local y de seguridad; no revertir revocaciones ni credenciales a un estado anterior.

La instalación de producción deberá proteger directorios de ejecutables, estado y actualización con permisos de Windows, ejecutar el servicio con privilegios mínimos y definir cómo firma y autoriza cada actualización. Los datos de farmacia y las credenciales no viajarán en los paquetes.

## Pendiente de decidir o proporcionar

- Canal confirmado: GitHub Releases. Se propone inicialmente `RafaViteka/Viteka_Utilidades`; comprobar si ofrece publicaciones públicas o elegir un repositorio público de distribución separado. No se ha creado ninguna publicación.
- Política confirmada: aviso y decisión del administrador; no instalación automática.
- Firma de código y clave de firma de manifiestos. Se gestionarán mediante almacén/CI seguro; no introducir claves privadas en el chat, código, ZIP ni carpeta compartida.
- Configuración TLS/LAN, servicio de Windows y pruebas de instalación, actualización y recuperación en equipos reales.

No se ha habilitado control remoto de equipos, publicado un instalador ni desplegado software a farmacias.

## Comprobación de publicaciones implementada

Configuración incluye «Buscar actualizaciones». Consulta la API HTTPS de GitHub y detecta una versión estable posterior con instalador Windows x64. La revisión se abre en el navegador solo por acción del usuario. No descarga ni ejecuta el instalador. No certifica la firma del paquete ni identifica al administrador. La aplicación todavía no realiza comprobaciones periódicas automáticas.

Repositorio inicial: `RafaViteka/Viteka_Utilidades`. Convención de publicación estable: etiqueta `vX.Y.Z` o `X.Y.Z` e instalador `VitekaUtilidades-Setup-X.Y.Z-win-x64.exe` dentro de los assets de esa misma publicación. Los instaladores actuales terminados en `-preview` no se ofrecen como actualización estable.

La llamada a GitHub usa un cliente HTTP independiente, sin la credencial del servicio local. No se distribuye un token personal de GitHub en el programa. El mecanismo actual requiere publicaciones accesibles públicamente; si el repositorio es privado, definir un canal público de distribución o un intermediario autenticado. Un HTTP 404 no se interpreta como «actualizado», porque también puede significar falta de acceso.

Pruebas ejecutadas mediante respuestas HTTP controladas: nueva versión, misma/anterior versión, prerelease, instalador ausente o externo, acceso denegado y contenido inválido. La consulta real desde cloud fue rechazada por el proxy antes de alcanzar GitHub; el dominio `api.github.com` se solicita en el borrador de red. Guardar el borrador no aplica automáticamente la política de red al proceso actual. No hay una publicación real validada.

El host también soporta instalación manual como servicio Windows; el instalador no la automatiza. Consultar [servicio Windows](servicio-windows.md). Antes de actualizar o desinstalar archivos, el instalador exige retirar ese registro de servicio; conserva el estado y requiere revisión administrativa antes de reinstalarlo.
