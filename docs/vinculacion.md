# Vinculación de puestos · prototipo local 0.3

## Alcance

La API exige una credencial para todos los endpoints `/api`. `/health` solo comprueba que el host está vivo; no comprueba autorización ni Oracle. El endpoint `/pairing/redeem` acepta códigos temporales. El servicio sigue escuchando únicamente en loopback: no se ha habilitado uso desde otros equipos.

La autorización queda vinculada a la credencial almacenada por usuario/equipo, no a una identidad personal de empleado. El cliente guarda la credencial cifrada mediante DPAPI `CurrentUser` bajo `%LOCALAPPDATA%\Viteka\Utilidades\device.dat`. Compartir el ejecutable no comparte esa credencial.

## Administración local

Desde la raíz del proyecto, tras compilar en Windows, ejecutar con la misma cuenta y el mismo directorio de estado que usa el host:

```powershell
dotnet src/Viteka.Server/bin/Release/net10.0/Viteka.Server.dll --admin-issue-code "$env:LOCALAPPDATA\Viteka\codigo-alta.txt"
```

El directorio de salida debe existir y ser privado del administrador. El comando crea un archivo nuevo, nunca sobrescribe otro, y no imprime el código en consola. En Linux se crea con permisos 600. En Windows hereda las ACL del directorio: validar que otros usuarios no puedan leerlo antes de generar un código. No guardar el archivo en el recurso compartido ni en el repositorio.

Abrir ese archivo localmente, introducir su código en Configuración → Vinculación del puesto y eliminar el archivo después de usarlo. No enviar códigos ni credenciales al chat.

- Código aleatorio de 128 bits: caduca en cinco minutos, un solo uso. Generar otro invalida el anterior.
- Cinco solicitudes de vinculación por minuto e IP durante la vida del proceso; al excederlas se devuelve HTTP 429. Este límite se reinicia al reiniciar el host.
- Credencial aleatoria de 256 bits: caduca en 90 días. Renovación actual mediante nueva vinculación.
- Si falla el guardado local después de consumir el código, generar otro y revocar la autorización huérfana.

Listar y revocar sin mostrar credenciales:

```powershell
dotnet src/Viteka.Server/bin/Release/net10.0/Viteka.Server.dll --admin-list-devices
dotnet src/Viteka.Server/bin/Release/net10.0/Viteka.Server.dll --admin-revoke-device ID_DEL_PUESTO
```

La revocación se consulta en cada petición, incluso con el host ya arrancado. La lista muestra identificador, nombre del equipo, caducidad y estado de revocación; no muestra hashes ni tokens.

## Estado del servidor

El registro se almacena por defecto bajo `%LOCALAPPDATA%\Viteka\Server` para la cuenta del host. `VITEKA_SERVER_STATE_DIRECTORY` permite señalar un directorio local privado distinto; el host y los comandos administrativos deben usar el mismo. El registro solo contiene hashes de códigos/tokens, nombres de equipos y metadatos. Se escribe bajo bloqueo y mediante reemplazo. Proteger el directorio con permisos de sistema; no colocarlo en el recurso compartido.

Los comandos administrativos actuales confían en el control de acceso del sistema operativo al directorio de estado. No hay consola administrativa remota ni se ha instalado todavía una cuenta de servicio.

## Validación

Ejecutado en Linux: compilación, quince comprobaciones de núcleo y smoke test HTTP con un registro temporal aislado. Se prueban código erróneo, consumo único, caducidad, sustitución de código, consumo concurrente, hashes persistidos, credencial caducada, revocación, acceso API sin token y límite de intentos.

Pendiente en Windows: vinculación visual, protección/recuperación DPAPI, ACL reales, acceso bajo la cuenta del futuro servicio y permisos de instalación. Antes de abrir la LAN se requieren TLS con verificación de certificados y configuración de firewall. No desactivar validación TLS ni reutilizar contraseñas del disco compartido. Oracle y las utilidades siguen sin implementar.
