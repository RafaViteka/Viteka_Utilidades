# Usuarios compartidos y actualizaciones · 0.12.2

La autoridad de usuarios reside en el servicio Windows `VitekaUtilidadesAccess` del servidor. Todos los puestos que utilicen su cliente y bootstrap comparten cuentas, roles y permisos; no hay registros de usuarios independientes por puesto ni se confía en el rol enviado por Angular.

## Roles

| Rol | Alcance | Gestión de contraseñas/cuentas |
| --- | --- | --- |
| Usuario | Inicio, aplicaciones asignadas, Actualizaciones de consulta y Mi cuenta | Cambiar su contraseña; no crear cuentas ni ejecutar el instalador |
| Administrador (titular) | Todas las aplicaciones, Favoritos, Actividad, Usuarios, Actualizaciones y Mi cuenta; sin configuración técnica | Crear/editar/desactivar usuarios normales, asignarles aplicaciones y restablecer sus contraseñas; cambiar la suya confirmando la actual |
| Superadministrador (Viteka) | Todo, incluida la configuración técnica | Crear/editar todos los roles y restablecer contraseñas ajenas; la propia se cambia confirmando la actual |

Las aplicaciones futuras se añaden al catálogo y el administrador marca quién puede usarlas. Un usuario normal no recibe automáticamente permisos sobre aplicaciones nuevas; los roles privilegiados acceden a todo el catálogo conocido. No se han creado utilidades de ejemplo para poblar el selector. El catálogo continúa vacío.

No se permite desactivar o degradar al último superadministrador activo. Los cambios de rol, permisos o contraseña revocan las sesiones anteriores. Las sesiones duran como máximo ocho horas y no se guardan en el cliente al cerrarlo.

## Cuentas iniciales solicitadas

Solo al crear por primera vez el registro de la farmacia:

| Cuenta | Contraseña inicial | Rol |
| --- | --- | --- |
| Viteka | jupiter | Superadministrador |
| F. titular | 1234 | Administrador |
| usuario | 0000 | Usuario |

Estas son credenciales de arranque conocidas, no una contraseña maestra permanente. Se exige cambiar la contraseña en el primer acceso. Las nuevas contraseñas tienen de 3 a 128 caracteres, sin requisitos artificiales de símbolos. Un restablecimiento por otra persona vuelve a exigir cambiarla. Primero completar la instalación en el servidor bajo control de Viteka; no entregar el acceso a puestos con esas credenciales iniciales.

Mientras quede una cuenta activa con su contraseña inicial de instalación, el servicio rechaza conexiones desde otros equipos. Viteka puede cambiar la suya y restablecer las de titular/usuario, o desactivar las que no vayan a utilizarse. Los restablecimientos ya sustituyen el secreto inicial aunque el destinatario deba cambiar la nueva contraseña temporal al entrar.

Las contraseñas se verifican con PBKDF2-SHA256, 600000 iteraciones, sal aleatoria individual y comparación constante. Cinco fallos bloquean temporalmente la cuenta; la API limita también los intentos por origen. No se almacenan contraseñas de usuarios en texto plano. No se recrean las cuentas iniciales al actualizar ni se reinicia silenciosamente un registro dañado o perdido.

## Instalación del servidor y puestos

El instalador, ejecutado en el servidor, registra el servicio bajo `LocalService` e inicia la autoridad HTTPS en TCP 7447. Los datos privados están bajo `%PROGRAMDATA%\Viteka\UtilidadesAccess\Private`: permisos de escritura/lectura solo para servicio y administradores del sistema, protección DPAPI LocalMachine, claves TLS y contraseñas Oracle incluidas. El registro de usuarios contiene verificadores PBKDF2, no sus contraseñas.

Se crea una CA privada de esta instalación y un certificado de servidor. El cliente valida cadena con esa CA, fechas, nombre y uso de servidor; no se instala una raíz global en Windows ni se acepta cualquier certificado. El firewall permite el puerto solo en perfiles Dominio/Privado y desde LocalSubnet. No se configura red pública ni acceso desde internet. La configuración inicial conocida sigue bloqueada para puestos hasta sustituir sus contraseñas.

`Cliente/access-server.json` y `Cliente/access-root.cer` son bootstrap público. Para ejecutar desde el disco compartido, distribuir la carpeta Cliente completa **con esos archivos**, en un recurso donde usuarios normales tengan lectura/ejecución y solo mantenimiento pueda modificar los binarios y el bootstrap. No copiar la carpeta Private ni las claves. El nombre del servidor del bootstrap debe resolverse desde los puestos. No instalar el paquete de servidor en cada cliente: abrir el cliente compartido y disponer de WebView2.

Las preferencias personales usan un perfil local por cuenta de aplicación en cada usuario Windows; no se mezclan favoritos/preferencias entre cuentas de aplicación. La configuración técnica y Oracle ya no residen en los perfiles de los puestos.

La prueba Oracle pasa a ejecutarse bajo LocalService: debe poder cargar el cliente ODBC y resolver el TNS del DSN de sistema. Al entrar Viteka en el servidor original, la configuración cifrada previa se envía por TLS al servicio y se elimina solo tras quedar disponible la configuración central. No se sobrescribe una configuración central ya existente. No se modifica el DSN ni la base de datos. Probar la conexión de nuevo desde Configuración tras actualizar.

La CA y los datos protegidos están ligados al servidor; su traslado a otra máquina requiere una migración administrada, no copiar archivos DPAPI. El certificado de servidor se renueva al iniciar el servicio cuando le quedan menos de 30 días; la raíz privada no necesita instalarse en el almacén global del equipo.

## Organización de Configuración

Configuración aparece para todos. Dentro, Mi cuenta reúne filas de contraseña, apariencia y pantalla inicial (esta última para roles privilegiados); cada ajuste abre un diálogo con Guardar/Cancelar. Usuarios solo aparece para titular y Viteka, con un icono de grupo distinto al de la cuenta personal. La conexión a NixFarma solo aparece para Viteka y se abre en diálogo. Entrar en Configuración no concede acceso a los ajustes técnicos: sus permisos en el servicio permanecen intactos.

## Actualizaciones e historial

Actualizaciones está dentro de Configuración, disponible para todos los roles. El aviso de nueva versión sigue abriendo directamente esta pantalla y Configuración muestra un indicador de versión pendiente. Se consulta GitHub cada 24 h con la app abierta y al abrir si la búsqueda está pendiente; con la app cerrada no hay notificación de escritorio. El instante de búsqueda persiste, evita peticiones duplicadas y permite búsqueda manual aunque todavía no hayan pasado 24 h. Una avería de red no se interpreta como «actualizado».

El aviso abre la pestaña; la instalación exige titular/Viteka, ejecutarse desde el servidor y confirmación/UAC. No se inicia un instalador de servidor desde un puesto remoto. Se vuelve a consultar la oferta real antes de descargar, conserva HTTPS, destinos permitidos, tamaño y SHA256; las credenciales del servicio nunca viajan a GitHub. El instalador para el servicio, reemplaza sus archivos y lo inicia de nuevo conservando usuarios, contraseñas, CA y Oracle. Los puestos deberán cerrar el cliente compartido antes de reemplazar sus archivos y volver a iniciar sesión después del reinicio del servicio.

Historial abre una ventana con las publicaciones y notas de GitHub como texto, y los eventos locales del perfil Windows: búsquedas, instalador iniciado y versión realmente ejecutada. «Instalador iniciado» no equivale a instalación confirmada; solo abrir la aplicación con una versión añade «Versión ejecutada». Los eventos empiezan en esta entrega; no se inventan instalaciones históricas. Las publicaciones anteriores sí se consultan desde GitHub.

## Validación

55 checks del núcleo cubren los tres roles, elevaciones prohibidas, listas de aplicaciones, contraseñas y revocaciones, bloqueo de cuenta, conservación al actualizar y calendario de 24 h. El navegador comprueba navegación por roles, cambio inicial, edición de usuarios/permisos, configuración oculta, Actualizaciones y notas sin ejecutar HTML. El workflow Windows instala/reinstala/retira el servicio bajo LocalService, exige salud HTTPS y conservación del registro, rechaza una CA ajena y ejecuta el cliente real con una autoridad aislada. El smoke nativo realiza login/cambio inicial con los tres roles y envía mensajes privilegiados directamente para verificar que son rechazados incluso sin pasar por la navegación.

La validación de red y TNS reales entre el servidor y los puestos de la farmacia debe realizarse allí. No se afirma haber probado esos equipos desde el entorno cloud. El paquete continúa siendo un canal de pruebas sin firma de editor.
