# Fuentes de tenencia de Málaga

`malaga-excel.csv` conserva íntegramente las 83 filas (A–D y fila de origen) de «existencias minimas.xlsx» aportado por el usuario, sin metadatos personales. No se fusionan ATC repetidos ni se modifica esta transcripción original.

SHA256 del XLSX: `16fa6e6800f816f1ad4df57aa51ecaca2a06115c1ef25dc6efd006737375b8e0`.

`malaga-video-articulos.csv` incorpora las listas de la derecha de los dos vídeos recortados aportados después: 2.089 asociaciones en 83 grupos, con 2.087 CN distintos. Dos CN pertenecen a dos grupos y se conservan en ambos. Cada registro incluye fila Excel, vídeo, fotograma y segundo aproximado. La verificación se refiere al **CN**; la denominación es una lectura parcial de referencia y puede contener errores de OCR en dosis/envases. No usarla como denominación clínica. El catálogo inicial utiliza un nombre abreviado sin cifras; la consulta reemplaza este texto por la descripción de NixFarma cuando está disponible.

`malaga-video-fuentes.json` identifica ambos clips con SHA256, cantidades, mínimos y fotogramas por grupo. Los vídeos se revisaron visualmente y mediante lecturas cruzadas; no se importan existencias, precios ni datos de la farmacia del vídeo.

La bolsa pediátrica se corrige a mínimo **2**, visible en el vídeo, frente al 4 de la columna C original. Bisturíes incorpora las 18 referencias del vídeo; el CN 123299 del Excel no aparece en esa lista y se conserva solo en el CSV original. La suma de mínimos combinados es 178; no sustituye la comprobación de condiciones por dosis/tamaño.

Pendientes de revisión: fentanilo exige ambas dosis (25 y 50 mcg/h), Foley ambos tamaños (CH16/CH18), y microenemas incluye en el vídeo el CN 718796 descrito como Valium inyectable. Estos tres grupos no acreditan cumplimiento automáticamente.

Generar con `python scripts/build-tenencia-catalog.py`; comprobar sin modificar con `python scripts/build-tenencia-catalog.py --check`. Las fuentes aportadas no equivalen a una revisión normativa oficial.
