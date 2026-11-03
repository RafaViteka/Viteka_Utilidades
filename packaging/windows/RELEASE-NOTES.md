# Viteka Conecta 0.18.23

Primera distribución estable para farmacias con instalación centralizada.

- El instalador de servidor utiliza `C:\Viteka\Conecta`, registra el servicio de acceso y prepara el cliente compartido.
- El instalador de puesto valida `X:\Viteka\Conecta\Cliente` y crea accesos directos sin copiar el servicio ni las credenciales de NixFarma.
- El superadministrador mantiene todas las funciones activas; administradores y usuarios reciben accesos expresamente.
- Tenencia admite los listados independientes de las provincias andaluzas y lectores EAN-13 con preámbulo `$`.
- Incluye consulta, cotejo, caducidades, borradores, historial, adjuntos y exportaciones.

La instalación conserva cuentas, configuración y datos al actualizar. El disco `C:` debe estar compartido por la farmacia de forma que los puestos vean la misma ruta como `X:`.
