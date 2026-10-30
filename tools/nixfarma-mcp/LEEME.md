# Viteka NixFarma MCP local · 0.1.1

Complemento STDIO local para Windows x64. No modifica ni actualiza Viteka Utilidades. Herramientas de lectura: probar_conexion, listar_tablas, describir_tabla, consultar_cn y buscar_articulos_por_atc. No ofrece SQL libre ni HTTP, no lee pacientes y no escribe en Oracle.

## Equipo necesario

La aplicación donde se añade el MCP debe ejecutarse en el mismo Windows que el conector y el DSN NIXFARMA de64bits (en esta farmacia, el servidor). Si la captura procede de otro PC, STDIO se ejecuta en ese PC y no accede al DSN del servidor. Instalar este conector únicamente no conecta automáticamente un chat ejecutado en la nube: tiene que aparecer como herramienta disponible en esa sesión.

## Instalación

1. Instalar Python3.12 de64bits desde https://www.python.org/downloads/windows/ incluyendo el lanzador `py`.
2. Extraer el contenido del ZIP en C:\Viteka\MCP. Deben quedar server.py, requirements.txt, instalar.ps1 y este LEEME directamente en esa carpeta.
3. Abrir PowerShell con el mismo usuario Windows con el que se ejecutará la aplicación del chat. No necesita Ejecutar como otro administrador.
4. Ejecutar:

```powershell
cd C:\Viteka\MCP
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\instalar.ps1
```

Bypass se aplica solo al proceso que ejecuta este script local. No cambia la política permanente ni deshabilita TLS/validación de paquetes. El instalador crea .venv, instala las3 dependencias versionadas y pide DSN, usuario y contraseña por consola. La contraseña no se muestra ni se pasa como argumento o variable de entorno. Prueba SELECT1FROMDUAL en transacción solo lectura y guarda la configuración únicamente si pasa. Usa explícitamente Windows Credential Manager (WinVault), sin fallback a un fichero de texto.

## Formulario de la captura

- Nombre: Viteka NixFarma
- Tipo: STDIO
- Comando para iniciar: C:\Viteka\MCP\.venv\Scripts\python.exe
- Argumentos: una sola entrada C:\Viteka\MCP\server.py
- Variables del entorno: ninguna
- Paso de variables del entorno: ninguna
- Directorio de trabajo: C:\Viteka\MCP

Guardar y activar el MCP en la sesión que vaya a usarlo. Pedir primero: «Usa Viteka NixFarma para probar la conexión. Después consulta el CN de seis cifras que te indique y comprueba sus grupos sin modificar datos». La configuración correcta se confirma cuando el cliente muestra las5 herramientas y probar_conexion devuelve conexion_verificada. No ejecutar server.py sin argumentos en una consola esperando un menú: sin argumentos espera mensajes MCP por STDIO.

## Diagnóstico y credenciales

```powershell
C:\Viteka\MCP\.venv\Scripts\python.exe C:\Viteka\MCP\server.py --probar
C:\Viteka\MCP\.venv\Scripts\python.exe C:\Viteka\MCP\server.py --configurar
C:\Viteka\MCP\.venv\Scripts\python.exe C:\Viteka\MCP\server.py --olvidar
```

No introducir credenciales en el formulario del MCP ni en el chat. La cuenta de consulta Oracle debe disponer de los permisos de lectura necesarios para las tablas indicadas. Si el controlador falla, solo se devuelven códigos SQLSTATE/ORA, sin texto bruto de conexión. No se cambia el DSN ni se conceden permisos en Oracle.

## Relación CN–ATC

Basado en las columnas exportadas por la farmacia: APPUL.AB_ARTICULOS (CODIGO,EMP_CODIGO,CLASE,GTE_CODIGO,GTETIP), APPUL.AB_GRUPOS_TERAPEUTICOS y BOTPLUS.BP_DBO_GTEATC. consultar_cn devuelve por separado artículo y grupos candidatos para comprobar claves, tipos, niveles y duplicados; no afirma que una FK no documentada esté validada. buscar_articulos_por_atc admite ATC humano nivel3/4/5 (4/5/7caracteres) y busca prefijos sobre códigos completos de nivel5. No incluye ATC veterinarios ni acredita restricciones de dosis/vía/tamaño. Si una tabla no es accesible, se devuelve error; no se sustituye silenciosamente por otra.

Cada operación abre conexión propia, desactiva autocommit, ejecuta SET TRANSACTION READ ONLY antes de leer y termina con rollback/cierre. Una sola operación concurrente, timeout de conexión10s y consulta15s (el controlador debe soportarlos). Respuestas limitadas con indicador truncado; no usar una muestra como listado completo. No expone credenciales mediante herramientas.

La conexión real y el almacenamiento Windows deben verificarse en el servidor. Las pruebas de desarrollo validan consultas parametrizadas/solo lectura y protocolo STDIO; no equivalen a probar Oracle de la farmacia.

## Corrección0.1.1

El DSN se envía sin llaves al Driver Manager de Windows; UID/PWD mantienen su escape de delimitadores. La versión anterior enviaba DSN={NIXFARMA}, que puede interpretarse como otro nombre y producir IM002 aun cuando SQLDataSources detecta NIXFARMA. No se cambia el DSN existente ni la contraseña. Extraer esta versión encima conservando .venv y volver a ejecutar --configurar. Si persiste IM002, ejecutar --diagnostico: muestra arquitectura y nombres de controladores, sin leer contraseñas ni consultar Oracle.
