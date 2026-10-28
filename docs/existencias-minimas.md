# Tenencia obligatoria · 0.13.1

## Fuente incorporada

El usuario aportó «existencias minimas.xlsx». Se transcriben las83 filas de Hoja1, filas2–84, con clasificación, nombre, mínimo de columna C y código adicional D. La transcripción CSV y SHA256 del archivo de origen están en data/tenencia. `python scripts/build-tenencia-catalog.py --check` compara el catálogo empaquetado con la generación reproducible a partir del CSV. El XLSX original permanece en los adjuntos; no se publica su metadato personal.

Se distribuye la lista de Málaga en Servidor/Catalogs/tenencia/29.json:83 grupos,75 selectores ATC,7 clasificaciones de productos sanitarios y1 CN explícito (123299). Los ATC repetidos no se fusionan: digoxina oral e inyectable, por ejemplo, siguen siendo grupos distintos. El total180 corresponde únicamente a la suma de columna C, no a una interpretación clínica de todos los mínimos.

La fuente es el Excel del usuario; no se afirma validación normativa oficial. Otras provincias siguen sin catálogo y no usan Málaga por defecto.

## Datos de farmacia y permisos

Configuración → Datos de la farmacia: nombre, provincia INE, localidad, correo, teléfono, dirección y código postal. Perfil central protegido en el servidor, compartido entre puestos y editable por titular/Viteka; usuarios normales no lo modifican. La provincia efectiva siempre procede de este perfil.

Aplicaciones → Tenencia obligatoria: titular/Viteka acceden por rol; usuarios normales necesitan asignación explícita. Se revalida el permiso en anfitrión, servicio y antes de devolver una consulta. Auditoría está dentro de Configuración; sigue sin un registro persistente de operaciones y no se inventan eventos.

## Vista

Todos los grupos aparecen en la columna izquierda, con clasificación, mínimo y estado. Al seleccionar uno, la derecha muestra sus medicamentos/coincidencias, CN y existencias. El catálogo está disponible antes de leer stock. Hay buscador, filtro de estado, detalle ampliado y fuente/versión/hora. No se consulta automáticamente al abrir. Sin consulta se muestra Sin consultar y no se presentan ceros como cumplimiento. Ante un error de stock se mantiene el catálogo, sin resultados de existencias anteriores.

Antes de buscar coincidencias en NixFarma, los grupos ATC no tienen una lista CN inventada; se explica que hay que consultar. El CN explícito sí aparece desde el inicio. Los grupos con condiciones específicas muestran sus notas de revisión.

## Búsqueda de coincidencias en NixFarma

Datos confirmados por el usuario: ARTICULOS y EXISTENCIAS; ARTICULO=CN, DESCRIPCION=nombre; ACTUAL o STOCK ACTUAL=stock. El Excel utiliza grupos ATC; no son los mínimos comerciales de ARTICULOS.

Solo lectura desde el servicio LocalService. SET TRANSACTION READ ONLY antes de cualquier inspección/consulta y rollback/cierre al terminar. Metadatos de tablas con WHERE1=0. Se detectan las variantes ACTUAL/STOCKACTUAL/STOCK_ACTUAL/«STOCK ACTUAL» de forma inequívoca. Identificadores validados y valores siempre parametrizados.

Se reconoce un campo ATC inequívoco por nombre (ATC/CODIGOATC/GRUPOATC). Si no existe, se examinan hasta20 campos cuyos nombres incluyen ATC, TERAP o GRUPO, con muestras de hasta201 filas por campo. Se requieren al menos2 valores ATC distintos coincidentes con la lista y un único campo candidato; la ambigüedad queda pendiente. No se recorre información de pacientes ni se hacen escrituras.

Después se buscan prefijos ATC mediante parámetros y se asocian los CN encontrados a cada grupo. Hasta10000 coincidencias, consultas de stock en lotes de250 CN y límite100000 filas. Los límites fallan de forma explícita, no se evalúa un listado truncado. Timeout de comando15s, presupuesto de lectura28s/espera30s, una sola operación activa si el controlador se bloquea. Respuestas hasta16MiB para albergar las asociaciones. La primera validación real de campos y valores se debe realizar en el servidor de la farmacia.

Configuración → Conexión a NixFarma → Comprobar tablas de tenencia (solo Viteka) muestra stock detectado, posibles campos ATC y nombres de las columnas de ARTICULOS, sin leer existencias. Estos nombres pueden copiarse desde el texto de diagnóstico para revisarlos con Viteka.

## Restricciones que no se adivinan

No se confunden las7 clasificaciones sanitarias de columna D con ATC ni CN: pendientes de enlazar. El CN123299 sí se usa directamente. No se suman existencias de varios almacenes/lotes sin confirmar su significado: filas duplicadas, artículo ausente y stock nulo quedan pendientes, nunca se convierten en cero.

Los títulos que restringen vía, forma o dosis llevan requiresReview. Aunque se muestren coincidencias ATC y sus stocks reales, el grupo no queda cubierto por sumar candidatos de otras vías o presentaciones. El fentanilo exige25 y50mcg/h,1 de cada; Foley exige CH16 yCH18. Requieren cerrar asociaciones/condiciones con la referencia del usuario, no simplemente comparar un total.

Última fila: texto «BOLSA DE RECOGIDA DE ORINA PEDIÁTRICA2 UNIDADES», columna C=4. Se conserva4 con aviso y estado pendiente. El usuario indicó que enviará el vídeo recortado para cerrar las asociaciones y esta diferencia. El vídeo original supera32MiB y no se ha visualizado; no se afirma haber extraído sus medicamentos.

## Validación

69 checks del núcleo incluyen las83 filas/minimos/ATC duplicados, catálogo antes del stock, asociaciones CN, coincidencias ATC parametrizadas, detección de campo terapéutico y rechazo de campos ambiguos, restricciones de vía/dosis, solo lectura, rollback y stock desconocido. Navegador: todas las asociaciones de grupo, catálogo de83 filas, dos digoxinas, CN123299, búsqueda/filtros, detalle, errores sin datos antiguos y permisos. Windows: catálogo visible con83 grupos aun sin conexión, perfil compartido, diagnóstico ODBC y conservación al actualizar. Datos ficticios exclusivamente en pruebas.
