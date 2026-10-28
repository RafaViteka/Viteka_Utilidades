# Viteka Utilidades

## Decisiones confirmadas

- Aplicación de escritorio para reunir utilidades para la farmacia conectadas a NixFarma.
- Diseño B: navegación lateral y acceso mediante tarjetas.
- Identidad de Viteka: logotipo aportado, isotipo junto a «Utilidades» en la barra superior y paleta de la newsletter.
- Organización visual familiar para usuarios de NixFarma 10.
- Compatibilidad requerida: Windows 10, Windows 11 y Windows Server 2022/2025.
- Archivos de aplicación alojados en una carpeta compartida del servidor; ejecución desde los puestos clientes.
- Acceso inicial a NixFarma de solo lectura; cualquier escritura se decidirá por utilidad.
- Primero se define la plataforma; las utilidades concretas se desarrollarán después.

## Estructura propuesta

| Sección | Función |
| --- | --- |
| Inicio | Accesos a herramientas, favoritos y estado de conexión. |
| Utilidades | Catálogo con búsqueda; cada herramienta declara sus permisos y requisitos. |
| Favoritos | Accesos elegidos por el usuario. |
| Actividad | Resultado de operaciones, usuario y fecha; sin credenciales ni datos clínicos en los registros. |
| Configuración | Farmacia, conexión, preferencias y diagnóstico. |

La configuración de conexiones será independiente de las utilidades. Una conexión no configurada o fallida no debe impedir abrir la aplicación: las herramientas que la necesiten mostrarán su estado y el motivo por el que no están disponibles.

## Separación técnica propuesta

1. Interfaz: navegación, búsqueda, formularios y estados de carga/error.
2. Aplicación: ejecuta cada utilidad y comprueba requisitos y permisos.
3. Conector NixFarma: centraliza conexiones, consultas parametrizadas, tiempos de espera y errores. No incluye reglas visuales.
4. Configuración local: preferencias y referencias a credenciales guardadas mediante el mecanismo seguro que se elija para el sistema operativo.
5. Registro de operaciones: estados y diagnósticos sanitizados.

No se presupone un esquema de base de datos ni se implementan escrituras sin definir su alcance. La política inicial de solo lectura está confirmada. Debe reforzarse con permisos de base de datos, además de restricciones en el conector.

## Pendiente de concretar

- Versiones/ediciones concretas de Windows y arquitectura del controlador, incluido el soporte necesario para Windows 10.
- Motor identificado: Oracle mediante ODBC. Pendientes la versión del servidor y la disponibilidad del controlador/DSN en cada puesto.
- Credenciales de base de datos con permisos de solo lectura; definir cómo se aprovisionan sin incluirlas en el paquete compartido.
- Usuarios, permisos, distribución y actualizaciones.
- Tecnología de escritorio, tras comprobar compatibilidad con el controlador de NixFarma.

## Prototipo actual

`prototipo/index.html` es un HTML autónomo con logotipos integrados. Se abre directamente en el navegador, sin instalación ni conexión a internet. Es una maqueta; los módulos, datos y estados de actividad son ejemplos. No conecta con NixFarma ni guarda credenciales.

## Entorno de desarrollo

El entorno cloud permitirá desarrollar y ejecutar los checks compatibles con Linux. La ejecución de escritorio y el controlador real se validarán también en Windows y en la red de farmacia.

No se han fijado dependencias ni scripts de instalación hasta elegir la tecnología. Los pasos de instalación y arranque se guardarán en «Editar entorno» después de ejecutarlos y verificarlos. Trabajar sobre el checkout existente; no crear worktrees salvo petición expresa.

## Despliegue compartido: requisitos confirmados y propuesta

La instalación central almacena el paquete en una ruta UNC, por ejemplo `\\SERVIDOR\Aplicaciones\VitekaUtilidades`. Cada puesto ejecuta su propia instancia del proceso desde esa ubicación; esto no implica un servicio remoto ni una aplicación web. La cuenta y los permisos efectivos de ejecución serán los del usuario del puesto. La ruta es ilustrativa, no una configuración real.

- Compartido: ejecutables, recursos y versión de la aplicación. Usuarios ordinarios con lectura y ejecución; escritura restringida a mantenimiento.
- Local por usuario: preferencias, favoritos y registros bajo `%LOCALAPPDATA%\Viteka\Utilidades`, sin escribirlos junto al ejecutable ni compartirlos accidentalmente entre usuarios.
- Credenciales: mecanismo protegido por usuario o arquitectura de servicio si se acuerda posteriormente; no guardar contraseñas en texto plano en el recurso compartido.
- Conexión: en el modelo de ejecución directa, cada puesto necesita resolver y alcanzar el servidor NixFarma y disponer del controlador compatible cuando sea necesario. Instalar el programa en el servidor no instala el controlador en los clientes.
- Actualizaciones: contemplar ejecutables bloqueados mientras haya puestos abiertos; definir un procedimiento de cierre o carpetas de versiones antes de automatizar cambios.

### Validación necesaria en Windows

1. Arranque por ruta UNC con usuario sin privilegios administrativos.
2. Dos puestos ejecutando simultáneamente, con preferencias y registros independientes.
3. Conexión de solo lectura desde el puesto real, con controlador y arquitectura adecuados.
4. Manejo de indisponibilidad de la carpeta compartida y de la base de datos.
5. Actualización sin sobrescribir archivos en uso.

Estas comprobaciones están pendientes; no pueden acreditarse con la maqueta HTML ni con el entorno Linux actual. La referencia de conexión aportada confirma Oracle mediante ODBC. La arquitectura de proceso deberá coincidir con la del DSN y controlador que se use.

## Referencia ODBC aportada

La captura del administrador ODBC muestra DSN de sistema llamados `NIXFARMA`, tanto de 32 como de 64 bits. Los nombres de controlador mostrados son `Oracle en ORAHOME11CLI32` y `Oracle en ORAHOME11`. La ventana de configuración indica servicio TNS `NIXFARMA`. Estos nombres no prueban la versión del servidor Oracle ni que la conexión esté actualmente operativa. No se ha ejecutado una prueba de conexión.

La casilla `Read-Only Connection` aparece desmarcada. No modificar a ciegas el DSN de NixFarma usado por otras aplicaciones. La protección principal debe ser una cuenta con permisos mínimos de lectura; el conector añadirá restricciones para no exponer operaciones de escritura. No se reutilizarán credenciales sin verificar sus permisos.

### Dirección técnica propuesta

Aplicación nativa Windows con WPF y una versión de .NET soportada para las versiones concretas de Windows elegidas. Conector mediante `System.Data.Odbc`, reutilizando el DSN de sistema, sin introducir inicialmente otro cliente Oracle. Preferencia por proceso x64 si todos los puestos disponen del DSN/controlador de 64 bits; de lo contrario habrá que evaluar una distribución x86. La decisión final de runtime y empaquetado exige verificar compatibilidad con el controlador Oracle instalado y los sistemas operativos concretos.

No hay conexión desde el entorno cloud a la red privada de farmacia. La validación real de Oracle se realizará en un equipo autorizado de esa red. Nunca incluir contraseñas en repositorio, HTML, logs o instrucciones de instalación.

## Actualización: ODBC disponible solo en el servidor

El usuario confirma que el DSN está solo en el servidor. Esto cambia la dirección técnica propuesta: evitar instalar el cliente Oracle en cada puesto y centralizar el acceso mediante un servicio en el servidor. Esta sección sustituye la propuesta de conexión directa desde cada puesto; las observaciones anteriores sobre ese modelo se mantienen como contexto técnico, no como requisito del despliegue elegido.

### Arquitectura recomendada, pendiente de implementación

`Puesto Windows → aplicación WPF desde carpeta compartida → servicio autenticado en servidor → ODBC NIXFARMA → Oracle`

- Cliente de escritorio: interfaz, navegación, preferencias y favoritos locales por usuario. Sin controlador Oracle ni credenciales de base de datos en los puestos.
- Servicio de Windows: API para la red de farmacia, acceso ODBC exclusivo y consultas de solo lectura definidas por utilidad. No aceptar SQL arbitrario del cliente.
- La carpeta compartida distribuye el cliente; no sustituye al servicio ni proporciona autenticación.
- El servicio se instala y ejecuta localmente en el servidor, con cuenta de servicio de privilegios mínimos y credenciales protegidas fuera de la carpeta compartida. La instalación requerirá permisos administrativos en Windows.
- La arquitectura x64/x86 del servicio debe coincidir con el controlador ODBC del servidor. El cliente no depende de esa arquitectura ODBC.
- Autenticación de usuarios/puestos, autorización, TLS y alcance de firewall deben definirse antes de habilitar acceso real. No exponer el servicio a internet.
- Definir límites de consultas, tiempos de espera y cancelación para evitar sobrecargar Oracle. No registrar datos clínicos ni credenciales.

### Pendiente de verificar

- Servidor concreto que aloja el DSN y ejecución del servicio bajo su cuenta real.
- Existencia de dominio Windows o grupo de trabajo, para elegir autenticación apropiada.
- Versión de Oracle, permisos de lectura y compatibilidad del controlador instalado.
- Runtime Windows, empaquetado y validación en Windows 10/11 y Server 2022/2025.

No se ha instalado un servicio, abierto puertos ni probado acceso real a Oracle.

## Acceso compartido y autenticación de puestos

El usuario indica que el disco del servidor está compartido y se accede con credenciales de Windows. Esto confirma autenticación para el recurso SMB, pero no permite determinar si existe dominio ni autoriza automáticamente peticiones a la API. No transmitir ni reutilizar las contraseñas del recurso compartido en la aplicación.

Para no bloquear el diseño por la presencia de un dominio, se propone una autorización de puestos independiente:

1. El administrador instala el servicio y configura el acceso de solo lectura a NixFarma en el servidor.
2. El cliente se abre desde la carpeta compartida. Si no está autorizado, muestra una pantalla de vinculación.
3. El administrador autoriza el puesto mediante un procedimiento de alta de un solo uso, con caducidad y límite de intentos.
4. La autorización local se protege mediante mecanismos de Windows y no se guarda en la carpeta compartida. El alcance por usuario/equipo se fijará al implementar el almacenamiento seguro.
5. El administrador puede revocar y renovar las autorizaciones. El servicio valida la autorización en cada petición.

El transporte debe usar TLS con verificación de certificados; definir su instalación/confianza para la red local sin desactivar validación. La autorización del puesto no equivale a identificar individualmente al empleado. Si se necesitan permisos y auditoría por empleado, deberá añadirse autenticación personal; no atribuir automáticamente acciones a personas a partir del nombre del equipo.

Este mecanismo está propuesto, no implementado ni validado. Se mantiene pendiente concretar la política de usuarios y permisos.

## Base implementada (0.1)

- .NET 10, SDK fijado a 10.0.403; WPF para escritorio y ASP.NET Core para el host central.
- Proyectos `Viteka.Desktop`, `Viteka.Server` y `Viteka.Contracts`.
- Identidad visual: isotipo en la barra superior y logo completo en el lateral.
- Navegación, búsqueda, favoritos y actividad de sesión en WPF. Favoritos/actividad aún no persisten.
- Conector representado por interfaz y una implementación «sin configurar». No se ha instalado ni usado un controlador Oracle en cloud.
- Host demo exclusivamente local; ninguna autorización de red de farmacia implementada todavía.
- Compilación cruzada del cliente en Linux validada; ejecución visual en Windows pendiente.

Los endpoints de ejecución devuelven HTTP 503 con `database_not_configured` para utilidades conocidas y HTTP 404 para identificadores desconocidos. No existe endpoint de SQL genérico ni consultas de farmacia implementadas. La comprobación del servicio en el cliente no se presenta como comprobación de Oracle.

## Persistencia local implementada (0.2)

El proyecto `Viteka.Core` centraliza la persistencia, separado de WPF para poder probarla en cloud. Se guardan favoritos, preferencia de abrir Favoritos al iniciar y las últimas 100 acciones locales en `%LOCALAPPDATA%\Viteka\Utilidades\state.json`. No se guarda texto de consultas, credenciales ni datos de pacientes. La actividad usa acciones e identificadores de utilidad conocidos; no constituye auditoría clínica ni de base de datos.

La escritura se realiza mediante archivo temporal, vaciado a disco y reemplazo, bajo un bloqueo entre instancias. Cada actualización relee el estado antes de modificarlo para conservar cambios de otras instancias. Los archivos corruptos o de esquema no compatible no se sobrescriben; se avisa al usuario y la interfaz continúa disponible. La confidencialidad depende de los permisos locales de Windows: el archivo no está cifrado y no debe contener secretos.

Ocho comprobaciones de comportamiento ejecutadas en Linux: reapertura, separación entre carpetas de usuario, actualizaciones concurrentes, eliminación de favoritos, límite de historial, rechazo de utilidad desconocida, conservación de archivo corrupto y conservación de versión futura. Pendiente la prueba de interacción y de permisos reales en Windows.

## Vinculación implementada (0.3)

El registro local de dispositivos gestiona códigos temporales de un solo uso, credenciales con caducidad y revocación. La API exige credencial excepto en salud y canje del código; este último tiene un límite de intentos. El cliente incluye vinculación y almacenamiento DPAPI por usuario de Windows. [Procedimiento y validación](vinculacion.md).

Se mantiene la restricción a loopback: implementar autorización no supone habilitar acceso LAN ni completar el despliegue de servicio Windows. La validación de DPAPI e interfaz en Windows sigue pendiente.

## Distribución requerida (0.4)

El usuario requiere un ejecutable instalable y está interesado en actualizaciones remotas/online. Se preparan publicación autocontenida Windows x64, configuración de instalador de vista previa y workflow manual de empaquetado. El actualizador online y la instalación de producción todavía no están implementados. Ver [distribución](distribucion.md).

## Política de actualizaciones confirmada

GitHub Releases y aprobación del administrador antes de aplicar. Implementada la comprobación manual y aviso de nueva versión estable; no hay instalación online automática ni publicación real creada. Ver docs/distribucion.md para convención de assets, validación y requisitos pendientes de firma y despliegue.

## Soporte de servicio Windows

Host integrado con Windows Service Control Manager mediante --service, estado separado bajo ProgramData y comandos de administración manual. Instalación/arranque con LocalService y permisos restringidos preparados en PowerShell; no ejecutados aún en Windows. Ver servicio-windows.md. La API continúa limitada a loopback.

## Prioridad vigente: MVP visual vacío (0.5.0)

Por indicación del usuario, antes de crear aplicaciones o vínculos a NixFarma se cerrará el MVP visual. El catálogo público está vacío; no hay tarjetas de stock/caducidades/compras ni actividad ficticia. La interfaz oculta los formularios de vinculación y conexión y deja la configuración de base de datos para una fase posterior. Se conserva el código backend existente sin ampliarlo.

Se rehacen los estilos nativos de WPF para acercarlos al diseño B: barra propia con isotipo, navegación oscura con selección lima, controles redondeados, fondos marfil y estados vacíos. El HTML de referencia se actualiza al mismo contenido vacío. Se conservan los archivos locales anteriores; los favoritos/historial de utilidades retiradas no reaparecen en el MVP. No iniciar trabajos de conexión o utilidades hasta revisar el MVP con el usuario.

## Decisión vigente: Angular + WebView2 (0.7.0)

Migración de la interfaz autorizada por el usuario. El MVP se representa en frontend/ mediante Angular 22 y CSS. La ventana WPF se conserva solo como anfitrión WebView2 y barra nativa; ya no renderiza las pantallas de utilidades. Preferencias y actualización permanecen en .NET detrás de un puente de mensajes con acciones limitadas. Ver docs/mvp.md. No ampliar NixFarma ni las utilidades antes de revisar el MVP.

## Centro de accesos editorial (0.8.0)

El usuario solicita adoptar el diseño de Viteka_Portal_Estudio_2026.html en toda la aplicación y usar Inicio para soporte y recursos. Se añaden los accesos definidos por el archivo, menú plegable y temas persistentes, marca inferior e iconos bicolor transparentes. No se añaden utilidades Oracle. Detalles y límites en docs/portal.md.

## Estado actual 0.12.0

La autoridad de usuarios es ahora central, con servicio Windows e identidad HTTPS por instalación, bootstrap público del cliente y roles comprobados en servidor y anfitrión. La conexión Oracle se configura y prueba en ese servicio, bajo LocalService; se migra desde el perfil original al entrar Viteka en el servidor. No hay consultas de negocio ni utilidades publicadas todavía. Las secciones históricas que describen conexión directa por puesto o ausencia de servicio se mantienen como contexto; consultar accesos.md para el modelo actual.
