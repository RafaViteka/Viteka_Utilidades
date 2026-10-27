# Servicio Windows de vista previa

El host soporta `--service` y la integración del ciclo de vida con el administrador de servicios de Windows. Sigue siendo una demostración local: no abre la LAN y no conecta con Oracle. El instalador actual no registra automáticamente el servicio.

## Preparación

Publicar el servidor x64 con scripts/publish-windows.ps1. El paquete incluye `Servidor/manage-service.ps1`. Instalar sus archivos en una carpeta protegida bajo Program Files, usando el instalador de evaluación. No registrar como servicio un ejecutable situado en una carpeta donde los puestos o usuarios ordinarios puedan reemplazarlo.

Cerrar primero el host de consola si ocupa el puerto 5187. En PowerShell elevado, desde la carpeta Servidor instalada:

```powershell
./manage-service.ps1 -Action Install -ExecutablePath ./Viteka.Server.exe
```

El script crea `VitekaUtilidadesPreview` con inicio automático y cuenta integrada LocalService, sin contraseña. Crea `%PROGRAMDATA%\Viteka\UtilidadesPreview\Server` con permisos de administración/SYSTEM y modificación para LocalService, comprueba arranque y salud local y verifica que el puerto pertenece al PID del servicio. No altera servicios con el mismo nombre que apunten a otro ejecutable. No modifica el firewall.

Para evitar reutilizar datos/permisos inesperados, Install no actúa si el directorio de estado ya existe ni sigue junctions en sus padres. Tras una desinstalación o fallo, ese directorio puede conservarse: debe revisarlo un administrador antes de reinstalar. No borrar ni restaurar a ciegas el registro, porque contiene revocaciones. La reinstalación con reutilización del estado todavía no está automatizada.

## Operación

```powershell
./manage-service.ps1 -Action Start -ExecutablePath ./Viteka.Server.exe
./manage-service.ps1 -Action Stop -ExecutablePath ./Viteka.Server.exe
./manage-service.ps1 -Action Restart -ExecutablePath ./Viteka.Server.exe
./manage-service.ps1 -Action Uninstall -ExecutablePath ./Viteka.Server.exe
```

La desinstalación elimina el registro del servicio, pero conserva su estado. No ejecutar los cuatro comandos seguidos: elegir la operación necesaria. Antes de actualizar o desinstalar los archivos mediante Inno, eliminar el servicio con Uninstall; el instalador bloquea la operación mientras el servicio esté registrado. La automatización de una actualización completa con conservación del estado sigue pendiente.

## Códigos de vinculación del servicio

En consola elevada, el mismo ejecutable puede administrar el registro del servicio:

```powershell
./Viteka.Server.exe --service --admin-list-devices
./Viteka.Server.exe --service --admin-issue-code "$env:LOCALAPPDATA\codigo-viteka.txt"
./Viteka.Server.exe --service --admin-revoke-device ID_DEL_PUESTO
```

El código se escribe en un archivo privado nuevo del administrador; no se imprime ni se coloca en el recurso compartido. El directorio de salida debe ser privado. El host de consola sin `--service` mantiene su propio estado en LocalAppData: no mezclar ambos registros. Si existe VITEKA_SERVER_STATE_DIRECTORY, prevalece como ubicación explícita; host y comandos deben coincidir.

## Comprobado y pendiente

Compilación del host y pruebas HTTP/núcleo ejecutadas en Linux. Publicación x64 preparada. El modo `--service` se rechaza en Linux explícitamente. La instalación, scripts PowerShell, ACL, LocalService, recuperación tras reinicio y ciclo de vida de Windows todavía no se han validado en un equipo Windows. No presentar el servicio como instalado ni listo para farmacia hasta completar esas pruebas y habilitar TLS/LAN y Oracle.
