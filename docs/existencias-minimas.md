# Tenencia obligatoria · 0.13.0

Primera utilidad solicitada por el usuario. Auditoría está en Configuración para titular/Viteka. Sigue siendo un espacio sin registro persistente de operaciones; este traslado no implementa ni inventa un historial.

## Datos de la farmacia

Configuración → Datos de la farmacia permite al titular y Viteka guardar nombre, provincia, localidad, correo, teléfono, dirección y código postal. El perfil reside protegido en el servidor y se comparte entre los puestos; no depende de la provincia que envíe un cliente. Se valida provincia INE y correspondencia del código postal si se indica. Los usuarios normales no modifican estos datos.

## Pantalla y permisos

Aplicaciones → Tenencia obligatoria. Titular y Viteka acceden por su rol; cada usuario normal requiere asignación explícita de esta aplicación en Usuarios. El permiso se comprueba de nuevo en anfitrión y servicio. La pantalla presenta disponibilidad del listado y botón Consultar; abrirla no consulta existencias automáticamente. Tras una consulta: resumen por grupos, búsqueda por grupo/denominación/CN, filtro de estado, tabla paginada (25), detalle en diálogo y hora/fuente/versión del listado. Mantiene identidad Viteka y organización de trabajo con filtros/tablas/detalles de NixFarma 10.

## Conexión de solo lectura

Datos del usuario: tablas ARTICULOS y EXISTENCIAS; ARTICULO identifica el CN; DESCRIPCION la denominación; ACTUAL o STOCK ACTUAL el stock. No se usa el mínimo comercial de artículos: las unidades exigidas proceden del listado provincial.

El servidor abre transacción de solo lectura antes de inspeccionar o consultar. Lee solo los metadatos de ARTICULOS y EXISTENCIAS (WHERE 1 = 0) para encontrar los nombres reales. Admite ACTUAL/STOCKACTUAL/STOCK_ACTUAL/«STOCK ACTUAL» si hay una única coincidencia. Los identificadores se validan; códigos como parámetros ODBC en lotes de 100, hasta 1000 CN y 10000 filas, timeout de comando 15 s, espera de respuesta 30 s y presupuesto de lectura. No acepta SQL, provincia ni lista enviados por el cliente. Siempre rollback/cierre. Un controlador bloqueado conserva una única operación activa; no crea reintentos ilimitados.

Configuración → Conexión a NixFarma → Comprobar tablas de tenencia, solo Viteka, permite comprobar esta estructura sin tener todavía una lista ni leer existencias. La prueba real bajo LocalService debe hacerse en el servidor.

Si hay varias filas de EXISTENCIAS para un CN, el resultado es ambiguo: no se suman almacenes/lotes hasta confirmar su significado. Ausencia de artículo, stock ausente o nulo y artículos duplicados quedan pendientes; nunca se sustituyen por cero. Los grupos se evalúan sumando las unidades de los códigos alternativos declarados en su lista. Antes de incorporar una lista se debe verificar que esa agrupación representa las reglas de la referencia; otros criterios necesitan representación explícita y pruebas.

## Listas provinciales: pendiente la referencia

El usuario confirmó tenencia obligatoria según provincia y pidió extraer los códigos por grupo y unidades mínimas del vídeo «tenencia obligatoria málaga unycop.mp4». El archivo sediment file_00000000b01c81f4add22d4d8ea8add1 excede 32 MiB, límite de descarga de la herramienta; no existe copia local del adjunto. No se ha visto este vídeo ni se han extraído códigos o mínimos. Se ha pedido una versión comprimida o capturas legibles. Sí se han revisado fotogramas de las promociones anteriores de NixFarma 10 como referencia de organización.

**No se distribuye ninguna lista provincial en esta entrega.** Málaga y las demás provincias muestran «Lista de tenencia pendiente», sin consulta ni evaluación. Esto no equivale a «no faltan artículos» ni a «cumple». Cambiar la provincia selecciona su lista correspondiente y nunca utiliza otra por defecto.

Las listas revisadas se distribuirán con las actualizaciones, bajo Servidor/Catalogs/tenencia/NN.json (código INE de provincia). Contrato: schema=1, province, version, source, reviewedOn, groups:[{id,name,minimum,codes:[CN]}]. Código nacional de 6–7 cifras; mínimo entero positivo; IDs y códigos de cada grupo únicos. Hasta 250 grupos, 100 códigos por grupo y 1000 distintos por lista. Falta de lista, provincia incorrecta, formato no válido y fecha de revisión futura bloquean la evaluación. El instalador reemplaza los catálogos distribuidos; los datos de farmacia y credenciales se conservan en ProgramData. No guardar listas locales distintas sin un proceso de revisión de Viteka.

## Validación y límites

Checks del núcleo: persistencia/validación del perfil, selección de lista por provincia, mínimos y códigos malformados, cálculo cubierto/falta, datos ausentes/ambiguos, parámetros, identificación de columnas, rollback y prohibición de leer sin establecer solo lectura. Navegador: formulario compartido, lista pendiente, ejecución explícita, resultados de prueba, búsqueda/filtro/detalle, errores sin resultados antiguos y usuario con/sin permiso. Los datos ficticios están exclusivamente en pruebas, nunca en el catálogo distribuido. Windows: perfil central, diagnóstico ODBC de DSN inexistente, pantalla con lista pendiente y denegaciones de permisos.

Estas pruebas no acreditan las tablas/valores reales de la farmacia ni la corrección de un listado normativo. La utilidad queda instalada y su motor preparado, pero la consulta de tenencia real requiere incorporar la referencia y validar la estructura con NixFarma en el servidor.
