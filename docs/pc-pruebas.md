# Revisión únicamente en el PC de pruebas

La edición `Viteka Utilidades - Pruebas` es un producto aislado a efectos de instalación. No identifica automáticamente a una persona que pulse un enlace ni utiliza su cuenta GitHub como licencia. No existe todavía un servicio de licencias comerciales.

| Recurso | Edición normal | PC de pruebas |
|---|---|---|
| Carpeta predeterminada | Program Files/Viteka/UtilidadesVistaPrevia | Program Files/Viteka/UtilidadesPruebas |
| Servicio | VitekaUtilidadesAccess | VitekaUtilidadesReviewAccess |
| Autoridad/datos | ProgramData/Viteka/UtilidadesAccess | ProgramData/Viteka/UtilidadesReviewAccess |
| Preferencias y WebView2 | LocalAppData/Viteka/Utilidades | LocalAppData/Viteka/UtilidadesPruebas |
| HTTPS | 7447, red local según configuración | 7448, únicamente localhost |
| Instalador | VitekaUtilidades-Setup-... | VitekaUtilidades-Test-Setup-... |

AppId de Inno Setup, identidad de barra de tareas, accesos directos y exclusión entre actualizadores también son independientes. El perfil se fija al compilar con `-p:VitekaReview=true`; no puede transformarse en una instalación normal borrando un archivo de configuración. El cliente de pruebas rechaza servidores remotos y el puerto del servicio normal. No se abre una regla de firewall LAN para pruebas.

## Primera instalación

1. Descargar el artefacto **Viteka-PC-Pruebas-instalador** desde la ejecución de GitHub Actions correspondiente. Es necesario iniciar sesión en GitHub para descargar artefactos. No utilizar el instalador normal anterior para esta prueba.
2. Extraer y ejecutar `VitekaUtilidades-Test-Setup-0.18.0-win-x64.exe` en el PC de pruebas. Se crea una instalación independiente. No copiar carpetas Private ni certificados de una farmacia.
3. Abrir **Viteka Utilidades - Pruebas**. Las cuentas de arranque son nuevas y exigen cambiar su contraseña, con las reglas habituales.
4. Configuración muestra **PC de pruebas → ID de este PC → Copiar identificador**. Compartir ese ID para dirigir futuras revisiones; no enviar contraseñas ni claves.

El ID es aleatorio y no contiene número de serie, nombre del equipo ni credenciales. El servicio lo guarda cifrado con DPAPI LocalMachine. Permanece al actualizar o desinstalar (los datos se conservan); una identidad dañada no se regenera silenciosamente. No copiarla a otro PC.

La nueva instalación contiene su propio catálogo y cuentas; no importa automáticamente datos ni configuración Oracle de la edición normal. Para leer Nixfarma, el PC debe disponer de su propio acceso ODBC autorizado. La edición de pruebas no se conecta al servicio de producción como alternativa. No se supone conectividad desde cloud.

## Revisiones dirigidas

Después de conocer el ID, las revisiones futuras utilizan una etiqueta estricta:

`review-v0.18.1-pc-<id-de-32-caracteres>`

El activo esperado es `VitekaUtilidades-Test-Setup-0.18.1-win-x64.exe`, con el digest SHA256 de GitHub. El actualizador de pruebas solo ofrece versiones posteriores cuya etiqueta contenga exactamente su ID. Otro PC de pruebas las omite; las versiones normales y la 0.17 omiten estas etiquetas porque no coinciden con su formato. El historial aplica el mismo filtro. No se envían tokens del servicio a GitHub; el ID se compara localmente con la información de las publicaciones.

Esto controla qué ofrece el actualizador. Un activo de un repositorio público sigue siendo descargable manualmente: restringir también la descarga o imponer licencias comerciales requeriría distribución autenticada. La primera instalación no puede llegar de manera automática y exclusiva a una 0.17 que todavía no conoce este mecanismo.

Este desarrollo no publica etiquetas ni Releases. Primero se instala el paquete de arranque y se obtiene el ID real; cualquier publicación posterior debe estar autorizada y dirigida a ese ID. No usar etiquetas generales `v...-preview...` para estas revisiones.

## Compilación y comprobaciones

`scripts/publish-review-windows.ps1` compila perfil aislado, pruebas del núcleo y ejecutables autocontenidos, y crea el instalador. No publica. Los outputs intermedios `obj-review`/`bin-review` son independientes de los normales para no mezclar perfiles.

El job `isolated-pc-review` instala un servicio normal de referencia en un runner vacío, instala la edición de pruebas, comprueba que solo escucha en loopback, ejecuta WebView2 real y verifica que actualización/desinstalación conservan el ID y las cuentas de pruebas sin modificar los archivos privados ni detener el servicio normal. No usa una farmacia real.
