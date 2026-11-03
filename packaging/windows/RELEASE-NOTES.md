# Viteka Conecta 0.18.24

Corrección de conectividad y edición para instalaciones centralizadas.

- Los puestos prueban el nombre y las direcciones IPv4 del servidor publicadas por la instalación, siempre con la identidad HTTPS privada de la farmacia.
- El inicio de sesión distingue un servidor inaccesible o que no responde del resto de errores.
- La autorización específica para configurar Tenencia se aplica también al editor de listados de la aplicación, permitiendo incorporar artículos a quien la tenga concedida.
- El certificado de servicio se actualiza con las direcciones de red actuales sin sustituir la autoridad privada ni las cuentas.

La instalación conserva cuentas, configuración y datos al actualizar. El disco `C:` debe estar compartido por la farmacia de forma que los puestos vean la misma ruta como `X:`.
