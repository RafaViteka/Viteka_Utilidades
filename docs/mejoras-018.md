# Mejoras posteriores a 0.17 · rama de desarrollo 0.18

Estado: implementación en rama separada. No es una publicación ni acredita pruebas con Oracle real. La base es main 7f14020 (0.17.0). No cambiar el cálculo vigente de Tenencia: CN ausentes no se muestran tras consultar, cuentan cero; las fichas se suman; Correcto/Falta. Las notas de presentación se conservan.

## Navegación y permisos

Siempre Inicio al abrir, iniciar sesión o recuperar sesión tras actualizar. Se conservan preferencias, favoritos, permisos, última consulta y selección de grupo. La preferencia antigua OpenFavoritesOnStart permanece compatible en disco pero no controla la entrada; se retira su control de la interfaz.

Tarjetas: apertura y estrella son botones hermanos en columnas separadas, sin posicionamiento absoluto. Comprobaciones en claro/oscuro, lateral abierto/plegado y distintas ventanas.

Configuración → Acceso a aplicaciones: seleccionar cuenta, cambiar casillas, Guardar permisos o Descartar. Titular administra usuarios normales; Viteka todos los roles; los privilegiados acceden a todas las utilidades. El servidor mantiene la autorización y revoca las sesiones al guardar. El cliente detecta la revocación en su siguiente petición o comprobación de sesión (cada minuto); elimina accesos visibles no autorizados. Un favorito no concede permisos. Guardado e impresión nativos vuelven a comprobar el acceso.

## Base local y actualización de listados

`Private/pharmacy.db`: SQLite local al servicio, FULL synchronous y transacciones. No colocar en SMB. No se migra el registro de cuentas, las claves TLS ni Oracle. Conservar ACL de Private, exclusivas para servicio/administradores. Copias de seguridad: usar SQLite backup o detener el servicio y copiar la base con sus archivos WAL/SHM; no copiar únicamente pharmacy.db mientras esté abierto.

Separación de base Viteka, vista efectiva local, historial, conflictos y recibos de paquetes. La primera carga importa los JSON personalizados de 0.17 conservando los originales; como no siempre puede acreditarse su base histórica, los marca como legado para revisión explícita. La última consulta solo se ofrece cuando coincide con el listado efectivo vigente; una consulta iniciada contra una versión anterior no puede reemplazar la caché actual.

Cada edición exige la revisión leída y genera un identificador nuevo, evitando colisiones por segundo. Los clientes anteriores siguen siendo compatibles pero deben actualizarse para aprovechar la comprobación explícita de revisión. Altas de grupos utilizan ID propio; el ATC nunca sustituye al ID. Las asociaciones se identifican por grupo y CN.

Los paquetes `.vcat` contienen operaciones incrementales upsert/delete sobre IDs estables, con el grupo actualizado y sus asociaciones (incluidas las retiradas); no reemplazan ciegamente el listado provincial. Incluyen ID, provincia, versión origen/destino, aplicación mínima, firma RSA-PSS/SHA256 e identidad de clave. Reintentos no duplican operaciones. Faltas en la secuencia se rechazan. La fusión compara base anterior, personalización y nueva base: campos locales se conservan y colisiones se presentan. Bajas de grupos personalizados se conservan como conflictos; historial y referencias de declaraciones no se pierden silenciosamente. Los borradores se filtran por la lista vigente y exigen revisión si cambió la versión.

Aplicar paquete, guardar revisión y auditar es una sola transacción. Recuperar una revisión crea una personalización; conserva base maestra y recibos, evitando que reaparezca una actualización ya recibida. Restaurar original usa la base Viteka actualmente instalada.

Editor → Versiones y distribución muestra versiones, conflictos y revisiones; permite buscar paquetes de datos en GitHub, descargarlos, importar, verificar, revisar cambios y aplicar. Se necesita configurar la clave pública de confianza. La búsqueda encuentra activos `tenencia-29-*.vcat` de publicaciones existentes; no hay paquetes nuevos publicados por este desarrollo. La importación es explícita y no instala ejecutables.

## Aprovisionar la herramienta maestra

Reutiliza el editor actual; no es un segundo producto. En una instalación editorial controlada, un administrador ejecuta una sola vez:

```powershell
.\Viteka.Server.exe --catalog-key-init 'C:\ProgramData\Viteka\UtilidadesAccess\Private'
```

Genera `catalog-master.pem` privada y `catalog-trust.pem` pública, sin imprimir secretos. Ejecutar en el servidor editorial con las ACL de Private; la privada solo debe existir allí. Distribuir por mantenimiento únicamente la pública a Private de las farmacias. La aplicación no descarga ni cambia una clave de confianza automáticamente. Las cuentas superadministradoras de farmacia no pueden preparar paquetes sin la clave editorial.

Editar listado → Versiones y distribución → Nueva versión → Preparar paquete maestro guarda el archivo firmado. Revisarlo y publicarlo como activo GitHub requiere autorización aparte. Este código no publica. Tras publicar, importar el mismo paquete en la instalación maestra para avanzar su base; publicar siguientes paquetes desde esa nueva base. Mantener claves y copias de seguridad fuera del repositorio.

La versión de cada farmacia se consulta en su servidor. No se ha añadido telemetría ni un inventario remoto global de instalaciones. La firma acredita origen e integridad, no validación normativa de los medicamentos del listado.

## Declaración y faltas

Cada formato abre Preparar exportación, con modalidad, criterios, fecha de consulta y versión. Controles Excel/CSV/PDF/Imprimir sin ayudas auxiliares junto al nombre; se mantienen errores y resultados.

Listado completo: todos los grupos y candidatos del catálogo, incluidos CN no dados de alta (cero). Selección de declaración independiente de las asociaciones, guardada por usuario/provincia en servidor, asociada a la versión. Los archivos indican Sí/No y la impresión Seleccionado/No seleccionado. Si cambia el listado, se conservan selecciones aún válidas y se pide revisarlas.

Solo faltas: únicamente grupos con falta; máximo diez candidatos por grupo, última compra descendente y CN como desempate. Sin fecha de compra, al final por CN hasta completar diez. No se usa venta ni se reutiliza la selección de pantalla, que sigue sin ocultar artículos con stock. Se informa si no existe columna inequívoca de compra. Ya no se considera una última entrada/recepción como prueba automática de compra.

Excel/PDF/impresión incluyen logo y contexto; CSV contiene columnas y texto (sin logo). Se neutralizan fórmulas en CSV. PDF utiliza WebView2; impresión registra solicitud, no impresión física. La selección no cambia existencias ni acredita automáticamente cumplimiento normativo.

## Auditoría

Centralizada en SQLite con ID de operación único e identidad resuelta por el servicio. Hora UTC, presentación local y filtros de fecha, usuario, utilidad, acción y resultado. Páginas de 50, cursor por ID; las inserciones no desplazan páginas anteriores.

Accesos, cambios de cuentas/permisos, consultas, configuración Oracle/farmacia, búsqueda, edición, restauración, paquetes de datos, borradores, guardados de archivos, solicitudes de impresión y eventos del actualizador. No se guardan contraseñas, cadenas de conexión ni errores brutos. Los errores Oracle mantienen códigos sanitizados.

Operaciones del servicio con resultado posterior (usuarios/Oracle, por ejemplo) registran intención antes de ejecutarse y finalizan el mismo evento. Si se interrumpe el proceso, queda En curso o interrumpida, sin inventar éxito. Los cambios de catálogo y su evento se confirman atómicamente. Los eventos de archivos son confirmaciones del anfitrión autorizado; no acreditan acciones físicas ni que un archivo se haya leído después. El ejecutable instalado no se confirma por iniciar el instalador: se registra al recuperar sesión y ejecutar la versión.

Conservación inicial 12 meses; Viteka puede cambiarla de 1 a 120 meses en Auditoría. Reducirla elimina registros fuera del plazo. La depuración ocurre al arrancar el servicio y al consultar auditoría.

## Actualizador

Preparación indeterminada; descarga con porcentaje real de bytes; verificación indeterminada; preparación/UAC e instalación indeterminadas. Cancelación antes de lanzar la instalación. Semáforos entre procesos impiden descargas o instalaciones simultáneas. Se mantienen HTTPS, tamaño, SHA256, roles, actualización desde el servidor, sesión protegida y UAC.

Un monitor WPF se ejecuta desde una copia temporal autocontenida, verifica nuevamente el instalador y mantiene informada a la persona cuando el cliente principal se cierra. No necesita instalar .NET aparte. Su salida registra el éxito del instalador, no confirma por sí sola el arranque del cliente. Si falla, muestra el error y conserva el registro del instalador. No hay porcentajes simulados para instalación. Esta parte debe validarse en Windows antes de distribuir.

## Clasificación terapéutica

El editor ofrece una búsqueda condicional, separada de agrupación homogénea. Comprueba metadatos de AB_ARTICULOS y AB_GRUPOS_TERAPEUTICOS y coincidencia de GTE_CODIGO/CODIGO, CLASE y GTETIP antes de usar el enlace. Niveles, padres, clase y tipo se devuelven de la fuente; no se inventan niveles ni se infieren grupos humanos a partir de otro tipo.

La selección identifica código/clase/tipo y exige unicidad. Incluir descendientes es explícito y usa la jerarquía GTEPAD con protección de ciclos y límites. Las coincidencias se seleccionan antes de añadir; el editor no duplica CN ya asociados. No se ha verificado físicamente esta relación en el servidor del usuario: un esquema distinto, permisos insuficientes o ninguna coincidencia producen diagnóstico, no resultados supuestos. BOTPLUS no se utiliza como relación alternativa no confirmada.

## Validación

Pruebas del núcleo: migración, edición/caché, firma, secuencia, conflictos, altas/cambios/bajas, reintentos, transacción interrumpida, recuperación e historial; auditoría central y paginación; reglas de compras y CSV con Node; interfaz con autoridad y Oracle simulados, navegación, geometría de tarjetas, permisos, modalidades y auditoría. Las pruebas nativas requieren Windows; pasar fixtures no demuestra existencias, fechas ni relaciones terapéuticas reales.
