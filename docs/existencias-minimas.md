# Tenencia obligatoria · 0.14.2

## Fuentes incorporadas

El Excel del usuario aporta los 83 grupos de Málaga, filas 2–84 de Hoja1. Dos vídeos recortados, «Diseño sin título (3).mp4» y «Diseño sin título (4).mp4», permiten completar las listas de medicamentos de la derecha: **2.089 asociaciones y 2.087 CN distintos**. Se han revisado también las primeras filas seleccionadas en azul y la continuidad de las listas al desplazarse. Los grupos no se fusionan aunque compartan ATC.

Fuentes CSV, SHA256 y procedencia por grupo/artículo en `data/tenencia`. El catálogo se empaqueta en `Servidor/Catalogs/tenencia/29.json`. `python scripts/build-tenencia-catalog.py --check` comprueba la generación reproducible desde ambas transcripciones. Los CN se verificaron visualmente; las denominaciones del CSV son parciales y no están verificadas en dosis/envases. El nombre inicial se abrevia sin cifras para no presentar esos detalles de OCR como información clínica. Nixfarma aporta la denominación completa al consultar.

La bolsa de orina pediátrica cambia a **2 unidades**, como muestra el vídeo, frente al 4 de la columna C original. Bisturíes contiene las **18 referencias** del vídeo; el CN 123299 del Excel no figura en esa lista y queda únicamente en la transcripción original. La suma de mínimos combinados es 178; no representa una interpretación clínica de condiciones por presentación.

La fuente es la referencia aportada por el usuario; no se afirma validación normativa oficial. Las demás provincias no heredan Málaga por defecto. No se importan existencias, precios ni datos identificativos de la farmacia del vídeo.

## Datos de farmacia y permisos

Configuración → Datos de la farmacia: nombre, provincia INE, localidad, correo, teléfono, dirección y código postal. Perfil central protegido, compartido entre puestos y editable por titular/Viteka. La provincia efectiva procede siempre de este perfil del servidor.

Aplicaciones → Tenencia obligatoria: titular/Viteka acceden por rol; usuarios normales necesitan asignación explícita. Se revalida el permiso en anfitrión, servicio y antes de devolver una consulta. Auditoría está dentro de Configuración; sigue sin un registro persistente de operaciones.

## Vista

Pantalla de trabajo compacta inspirada en la organización de Nixfarma 10, con identidad Viteka. Cabecera de acciones, búsqueda/estado/resumen en una banda, tablas centrales con cabeceras fijas y desplazamiento independiente. Panel inferior con grupo, clasificación y artículo seleccionado, y resumen de mínimo/existencias/faltas a la derecha. Las flechas permiten recorrer grupos/artículos. Fuente y versión se despliegan al pie. La pantalla ocupa el área disponible sin la cabecera genérica ni el pie de otras secciones.

Los 83 grupos aparecen a la izquierda con mínimo, clasificación y estado. Al elegir uno, la derecha muestra todos sus medicamentos asociados por CN. El catálogo completo aparece antes de consultar existencias, con nombres abreviados y estado Sin consultar. Hay búsqueda por grupo, nombre o CN, filtro de estado y detalle ampliado. No hay consultas automáticas al abrir. Los errores conservan el catálogo y eliminan las existencias anteriores.

## Consulta de Nixfarma

Datos confirmados por el usuario: ARTICULOS y EXISTENCIAS; ARTICULO=CN, DESCRIPCION=nombre; ACTUAL o STOCK ACTUAL=stock. Todos los grupos de este catálogo disponen ya de una lista CN explícita. El resolver usa esa lista sin ampliarla por ATC: no necesita encontrar una columna ATC para esta consulta. ATC y clasificación sanitaria se conservan como referencia. La resolución ATC sigue disponible para futuros catálogos sin códigos explícitos.

Solo lectura desde el servicio LocalService y conexión central protegida. SET TRANSACTION READ ONLY antes de metadatos/consulta, rollback y cierre al terminar. Metadatos con WHERE 1=0. Stock ACTUAL/STOCKACTUAL/STOCK_ACTUAL/«STOCK ACTUAL» se detecta sin ambigüedad; identificadores validados y valores parametrizados. No se recorre información de pacientes ni se escribe en Nixfarma.

Hasta 10.000 códigos distintos, consultas de stock en lotes de 250 CN y límite de 100.000 filas. Máximo 1.000 códigos por grupo. Los límites producen error explícito; no se evalúa un listado truncado. Timeout de comando 15 s, presupuesto de lectura 28 s/espera 30 s, una sola operación activa si el controlador se bloquea. Respuestas de hasta 16 MiB. La validación de campos y existencias reales necesita el servidor de la farmacia.

Configuración → Conexión a Nixfarma → Comprobar tablas de tenencia (solo Viteka) muestra stock detectado, posibles campos ATC y nombres de columnas de ARTICULOS, sin leer existencias.

## Condiciones pendientes

Tres grupos conservan `requiresReview` y no pueden quedar cubiertos mediante una suma general:

- Fentanilo: exige 1 envase de 25 y otro de 50 mcg/h. Se muestran las 30 referencias, pero falta evaluar cada dosis por separado.
- Foley: se muestran las 31 referencias, pero debe comprobarse la condición CH16/CH18 por tamaño.
- Microenemas de diazepam: el vídeo incluye CN 718796 descrito como VALIUM 10 mg 5 ampollas. Se conserva la asociación visible, señalada para confirmar su inclusión.

Filas duplicadas de stock, artículo ausente o stock nulo permanecen pendientes: no se suman almacenes/lotes sin confirmar su significado ni se convierte lo desconocido en cero. Estos casos pueden dejar otros grupos sin evaluar tras la consulta.

## Validación

74 checks del núcleo: catálogo, 83 grupos, 2.089 asociaciones, mínimos combinados, productos sanitarios explícitos, ausencia de ampliación por ATC, condiciones pendientes, solo lectura, rollback y stock desconocido, además de accesos/actualizaciones. Navegador: 83 grupos reales, digoxinas separadas, 18 bisturíes, 3 bolsas pediátricas, filtros, detalle y errores sin stocks antiguos. Windows: servicio TLS, WebView2, perfil compartido, catálogo sin conexión, diagnóstico ODBC e instalación/actualización conservando cuentas y datos. Los stocks ficticios solo se usan en pruebas; pasar estas pruebas no confirma existencias reales de una farmacia.

Los errores Oracle en lectura/diagnóstico identifican la operación y muestran códigos SQLSTATE/ORA válidos, si existen; nunca texto bruto del controlador ni credenciales. Probar conexión y Comprobar tablas son operaciones distintas y sustituyen los mensajes anteriores.

## Lectura directa por CN · 0.14.2

Se conservan los 2.089 enlaces del catálogo y se consultan sus 2.087 CN distintos sin resolver ATC. La producción usa APPUL.AB_ARTICULOS.CODIGO/DESCRIPCION y la vista APPUL.AB_ARTICULOS_FICHA_E.ART_CODIGO/EXI_ACTUALES, campos confirmados en los metadatos exportados por el usuario. Se corrigen los nombres genéricos ARTICULOS/EXISTENCIAS que no correspondían a esta instalación. La vista aporta la información de existencias; no se supone el nombre de una columna en AC_EXISTENCIAS que no figure en los datos aportados.

Cada CN viaja como parámetro ODBC, conservando ceros iniciales. Las consultas de descripción y stock son independientes y no multiplican existencias por joins. Cero es un stock válido, distinto de nulo/artículo ausente. Si hay varias filas por empresa/almacén, queda pendiente y no se suman sin criterio confirmado. Sigue SET TRANSACTION READ ONLY, rollback y límites. Los adaptadores genéricos se conservan para las comprobaciones previas, pero el servicio selecciona explícitamente el esquema nativo.

Los textos adjuntos contienen una columna EXI_ACTUALES y tres copias idénticas ART_CODIGO, 20.466 valores cada columna. No se emparejan por posición ni se importan como stocks reales: solo una consulta por clave asegura la correspondencia. Falta validar la lectura en el servidor real.

## Códigos, ordenación y pendientes · 0.14.2

Código de clasificación visible en cada fila de grupo y junto al nombre en la cabecera/detalle; buscador incluye clasificación. Cabeceras de Código/Grupo/Mínimo/Estado y CN/Medicamento/Existencias permiten ordenar ascendente/descendente con aria-sort y teclado. Stocks numéricos, cero incluido, se ordenan por valor; desconocidos al final en ambos sentidos. Ordenar conserva la selección. La ventana ampliada comparte ordenación de artículos.

El lector distingue not_found (CN ausente de artículos), stock_not_found (sin fila de existencias), stock_null (valor nulo), duplicate_article y ambiguous_stock. Los ambiguos se devuelven con stock nulo, sin presentar arbitrariamente el de la primera fila. Los motivos se ven en la fila del medicamento; el panel inferior explica cuántos artículos impiden calcular el grupo. Se conserva la regla de cumplimiento: requiresReview o algún dato desconocido/ambiguo deja pendiente el total y faltas; no se inventa un cero. Un cero informado sigue siendo un dato conocido.

## Sin existencias pendientes · 0.15.1

- Un CN que no está dado de alta en Nixfarma, sin ficha de existencias o con el stock vacío cuenta como 0 unidades. El motivo se muestra como información junto al artículo, pero nunca deja el artículo ni el grupo pendiente.
- Si un CN tiene varias fichas de existencias, se suman.
- Todo grupo consultado queda como **Cubierto** (check verde) o **Faltas** (advertencia amarilla). Los tres grupos con condiciones de dosis o tamaño (fentanilo 25/50, diazepam microenemas y sondas Foley CH16/CH18) muestran **Revisar** con la misma advertencia cuando la suma llega al mínimo, porque la suma no demuestra cada dosis o tamaño por separado. Si no llegan al mínimo, aparecen con faltas.

## Correcto o falta · 0.15.2

- Los CN que no están dados de alta en Nixfarma no aparecen en la lista de medicamentos del grupo.
- Los medicamentos se ordenan por defecto de más a menos existencias (a igualdad, por nombre).
- Solo hay dos estados: **Correcto** (check verde) y **Falta** (advertencia amarilla). En la tabla de grupos se muestra solo el icono; el nombre del estado aparece al pasar el ratón. Los grupos con condiciones de dosis o tamaño se evalúan por la suma y conservan su nota informativa.

## Caducidad, exportación y listado editable · 0.17.0

- Columnas de caducidad, última compra, última venta y agrupación homogénea se detectan automáticamente en Nixfarma (Configuración → Conexión → Comprobar tablas las muestra). Si no se detecta la caducidad, la columna no aparece.
- La caducidad solo se muestra en artículos con existencias > 0: verde (> 6 meses), amarillo (≤ 6 meses), rojo (caducado), gris (sin fecha). Si solo hay mes y año se toma el último día del mes.
- Grupos con más de 10 artículos: se muestran todos los que tienen existencias y, hasta 10, los de compra o venta más reciente. Antes de consultar se muestran los 10 primeros. «Ver todos (N)» despliega la lista completa.
- La última consulta correcta se guarda en el servidor (tenencia-ultima-<provincia>.json) y es la misma para todos los puestos. Se descarta si cambia el listado.
- El superadministrador edita el listado; los cambios se guardan como tenencia-<provincia>.json junto a los datos del servidor y «Restaurar original» vuelve al listado empaquetado.
- Búsqueda de artículos: por CN (hasta 1.000), por descripción (hasta 5 palabras, consulta parametrizada) o por agrupación homogénea (solo columnas detectadas). Máximo 200 resultados.
- Importación masiva: «Código grupo;Grupo;Mínimo;CN;Descripción» o «grupo;CN».
