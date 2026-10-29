# Conexión local a NixFarma · 0.11.1

La aplicación incorpora ahora Oracle ODBC x64 y una prueba real de conexión, independiente de las utilidades. Las versiones hasta 0.10.1 solo tenían un marcador de conexión pendiente: instalarlas en un servidor no iniciaba ninguna conexión Oracle.

## Prueba en el servidor de la farmacia

1. Actualizar Viteka e iniciarla con el usuario de Windows que vaya a realizar la prueba en el servidor.
2. Abrir Configuración → Conexión a NixFarma → Configurar conexión.
3. Conservar el DSN de sistema `NIXFARMA` si es el correcto. Introducir usuario y contraseña **de Oracle**, facilitados por el administrador con permisos mínimos de lectura. No son las credenciales del disco compartido.
4. Guardar y pulsar Probar conexión. Se comprueba el DSN de sistema de 64 bits en este equipo y se informa de su ausencia. La prueba usa realmente ODBC, abre una transacción, ejecuta primero `SET TRANSACTION READ ONLY`, después `SELECT 1 FROM DUAL` y termina con rollback/cierre. No consulta tablas de farmacia.
5. Si falla, comunicar el mensaje y los códigos SQLSTATE/ORA, sin enviar contraseñas ni cadenas de conexión.

Guardar la configuración no equivale a verificarla. Un resultado correcto acredita la conexión y esa consulta de prueba en ese instante; no verifica el esquema ni todos los permisos de futuras consultas. Estas deberán definirse por utilidad. La cuenta Oracle debe tener permisos mínimos aunque la prueba fuerce su transacción de solo lectura. No se ejecutan escrituras, concesiones de permisos ni cambios de DSN.

## Credenciales y límites

- El formulario de credenciales es WPF, con PasswordBox; los mensajes Angular solo llevan identificadores de acciones, nunca SQL, usuario o contraseña.
- `nixfarma.dat` vive bajo `%LOCALAPPDATA%\Viteka\Utilidades`, cifrado con DPAPI CurrentUser y escritura temporal/renombrado. No se guarda en la carpeta compartida ni se envía a GitHub. No se devuelve la contraseña en el estado de conexión ni en errores.
- La configuración solo se puede utilizar bajo ese usuario de Windows y ese equipo. No trasladarla a LocalService ni a otros puestos: su configuración segura se diseñará al integrar el servicio. Otro usuario debe configurar su conexión.
- El formulario permite conservar una contraseña dejando el campo vacío únicamente si no cambian DSN ni usuario. Olvidar conexión elimina solo la configuración local cifrada, con confirmación nativa.
- Si una configuración está dañada o pertenece a otra identidad, se conserva y no se sobrescribe silenciosamente; la interfaz permite olvidarla y configurar de nuevo.
- Errores de controlador se convierten en mensajes y códigos seguros; no se devuelve el mensaje original que podría incluir detalles sensibles. Casos: IM002 DSN ausente, IM003/IM014 controlador/arquitectura, ORA-01017 credenciales, ORA-28000 bloqueo, ORA-28001 caducidad, ORA-12154 resolución TNS, ORA-12514/12541/12545/12170 disponibilidad y ORA-01031 permisos.
- Los comandos tienen timeout de 15 s. La interfaz deja de esperar tras 30 s y permanece disponible. Si un controlador ignora los timeouts de apertura, su trabajo puede continuar en segundo plano: no se admiten nuevas pruebas concurrentes hasta que termine. No se fuerza la terminación de otros procesos de Oracle.
- Se abre y cierra una conexión por prueba. No hay sesión permanente ni consulta automática al arrancar. Las futuras utilidades reutilizarán la configuración y la separación de lectura, con consultas aprobadas y parametrizadas.

## Validación y alcance

Los checks del núcleo verifican el orden de lectura, el bloqueo de consulta si falla el modo read-only y el rollback ante fallo. El navegador verifica estados, mensajes, configuración/prueba/eliminación simuladas y ausencia de campos de contraseña o SQL en el puente. El smoke Windows prueba el formulario real con credenciales ficticias, cifrado/descifrado DPAPI, ausencia de contraseña en el estado, eliminación y error real IM002 mediante ODBC con un DSN inexistente. No se crea ni modifica un DSN del runner.

La prueba real contra el servidor Oracle de la farmacia debe realizarse allí: no es accesible desde el entorno cloud. Esta entrega no añade todavía consultas de negocio, acceso LAN, TLS, autorización de puestos ni actualización de un servicio instalado. El cliente ejecutado en otro puesto sin Oracle/DSN mostrará que falta el DSN; ejecutar el exe desde un disco del servidor no cambia dónde se ejecuta el proceso.

## Actualización 0.12.0: autoridad central

La sección local anterior describe 0.11.x. Desde 0.12.0, solo Viteka puede abrir Configuración y la conexión se protege y ejecuta en el servicio central del servidor bajo LocalService. La configuración del perfil original se migra al entrar el superadministrador en ese servidor. Ver [usuarios compartidos](accesos.md), incluido TNS, bootstrap, permisos y pruebas tras actualizar.

Desde 0.14, el diálogo elimina resultados anteriores al iniciar una acción y bloquea acciones concurrentes. Probar conexión verifica una consulta fija; Comprobar tablas verifica la estructura y puede fallar aun con conexión correcta. Los fallos de tenencia muestran los códigos SQLSTATE/ORA proporcionados por el controlador, con mensajes fijos sin datos de conexión. ORA-00942 señala acceso/esquema de tabla y ORA-00904 una columna no reconocida; deben revisarse con el diagnóstico del servidor, sin modificar tablas.
