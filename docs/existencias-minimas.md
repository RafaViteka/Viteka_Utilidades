# Primera utilidad: consulta de existencias mínimas

## Solicitud y estado

El usuario solicita una consulta de existencias mínimas, tomando como referencia el vídeo «tenencia obligatoria málaga unycop.mp4», con la organización visual de NixFarma 10 y la identidad Viteka. También solicita agrupar Auditoría en Configuración.

El vídeo adjunto, sediment file_00000000b01c81f4add22d4d8ea8add1, excede el límite de transferencia de 32 MiB de la herramienta. No se ha podido visualizar y no hay copia en los adjuntos locales. Se ha solicitado un vídeo comprimido o capturas. No afirmar que se han revisado sus pantallas.

Falta confirmar si el criterio es tenencia obligatoria de Málaga, mínimos de stock de la farmacia o ambos. No son equivalentes: el primero requiere una lista y reglas normativas verificadas; el segundo requiere los umbrales de NixFarma. No asumir una lista legal ni importar datos de otras farmacias.

La conexión Oracle de prueba existe, pero no se conocen tablas, campos y relaciones de artículos/existencias/mínimos. Una conexión correcta y SELECT 1 FROM DUAL no prueban que la consulta de negocio esté definida.

## Propuesta visual pendiente de contrastar con el vídeo

Mantener la barra lateral, los colores, temas e iconografía de Viteka. Dentro de Aplicaciones, una pantalla de trabajo compacta con título, filtros a acordar, acción Consultar y tabla de resultados. Los detalles de una fila se abrirían en ventana emergente. Diferenciar «sin consultar», «sin resultados», «conexión no disponible» y error; no mostrar una tabla vacía como si una consulta bloqueada hubiera terminado bien.

La elección de filtros, columnas, agrupaciones, colores de estado y exportaciones se cerrará al revisar la referencia y el criterio. No se ha añadido un catálogo de ejemplos ni resultados ficticios a la aplicación.

## Información necesaria para integrar NixFarma

- Tablas/vistas y columnas reales para identificador/código nacional, denominación, existencias y mínimo cuando corresponda.
- Relación entre artículo, almacén/ubicación y existencias; significado de las unidades y tratamiento de cantidades pendientes si corresponde.
- Si se trata de tenencia obligatoria: fuente vigente, fecha, ámbito y reglas de equivalencia/agrupación de presentaciones.
- Un ejemplo conocido para comprobar el resultado en la farmacia, sin contraseñas ni datos personales.

Implementación cuando se confirme: componente Angular independiente, operación tipada específica en el servicio HTTPS, permiso por ID de utilidad revalidado en servidor, conexión central protegida y consultas parametrizadas de solo lectura con límites y timeout. Ver primera-aplicacion.md.

## Entregado por separado

0.12.3 mueve Auditoría a Configuración para titular/Viteka y actualiza navegación y comprobación de roles. Auditoría sigue sin un registro persistente de operaciones: no confundir el traslado de su pantalla con la implementación del registro. La utilidad de existencias sigue pendiente de los datos anteriores.
