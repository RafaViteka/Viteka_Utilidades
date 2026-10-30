Consulta directa de descripciones y existencias por los códigos nacionales ya incluidos.

- Se conservan los 83 grupos y sus 2.089 asociaciones; esta lista no necesita buscar grupos ATC en NixFarma.
- Descripción desde APPUL.AB_ARTICULOS, por CODIGO y DESCRIPCION.
- Stock actual desde APPUL.AB_ARTICULOS_FICHA_E, por ART_CODIGO y EXI_ACTUALES, campos presentes en los metadatos aportados.
- Corrige el uso anterior de nombres genéricos ARTICULOS/EXISTENCIAS que no correspondían a estas tablas reales.
- Consultas parametrizadas de solo lectura. Stock cero se conserva; datos ausentes o ambiguos quedan pendientes. No se emparejan las columnas pegadas por su posición ni se suman almacenes sin confirmar.

Actualizar desde Configuración → Actualizaciones en el servidor, con los clientes cerrados, sin desinstalar. Después abrir Tenencia obligatoria y pulsar Consultar existencias. La cuenta Oracle debe poder leer ambas fuentes. La consulta real debe comprobarse en el servidor de la farmacia.
