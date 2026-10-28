# Preparación de la primera aplicación

Estado 0.12.2: esta es una propuesta para revisar con el usuario. El catálogo sigue vacío; no se han creado consultas, tablas supuestas, permisos de ejemplo ni una utilidad funcional.

## Diseño recomendado

- Catálogo de Aplicaciones con tarjeta por utilidad: nombre claro, finalidad en una frase e icono propio. Mostrar solo las aplicaciones autorizadas.
- Abrir la utilidad en una pantalla completa dentro de Viteka, conservando navegación y un enlace «Volver a Aplicaciones».
- Cabecera compacta con nombre y breve explicación. Debajo: filtros relevantes y botón Consultar. No consultar automáticamente al abrir.
- Resultados en tabla con cabecera fija, columnas alineadas según el dato, orden y paginación. Mostrar número de resultados y fecha de consulta; diferenciar resultados vacíos, todavía sin consultar, carga y error.
- Ventana emergente para ver el detalle de una fila o confirmar una acción concreta. Mantener tablas extensas y procesos con varios pasos en la pantalla principal.
- Respetar marfil, petróleo, temas y zoom actuales. En ventanas estrechas, permitir desplazamiento horizontal de la tabla sin desbordar toda la aplicación.
- Incorporar exportación, selección múltiple o indicadores de resumen solo cuando la utilidad los necesite y se acuerde qué datos incluyen.

## Estructura de implementación

| Capa | Responsabilidad propuesta |
| --- | --- |
| Contratos | ID estable de la utilidad, filtros tipados, resultado tipado y paginación. Añadir una entrada real a DemoCatalog cuando se implemente. |
| Angular | Componente propio bajo frontend/src/apps/<id>/, estados de pantalla y navegación desde Aplicaciones. Evitar seguir acumulando cada utilidad en app.html. |
| Puente nativo | Acción explícita y limitada a esa utilidad, reutilizando sesión nativa. Nada de SQL ni credenciales Oracle desde la interfaz. |
| Servicio | Operación autenticada por utilidad; revalidar en el servidor el permiso de su ID antes de consultar. Publicarla en el catálogo no sustituye proteger la operación. |
| NixFarma | Lector específico en el servidor, reutilizando el perfil Oracle central protegido. Consultas parametrizadas con transacción de solo lectura, timeout, cancelación y límites de filas/concurrencia. |
| Permisos | Registrar el ID en el catálogo para que el titular/Viteka pueda asignarlo. Los usuarios normales no reciben acceso automático. Los roles privilegiados acceden al catálogo conocido. |
| Pruebas | Datos ficticios representativos, permiso concedido/denegado, filtros/límites y resultados vacíos/errores; luego prueba de lectura en el servidor real. |

`OracleService` hoy configura y prueba la conexión; no ofrece aún consultas de negocio. `IPharmacyReader` pertenece al host anterior de regresión y no constituye una API de utilidades ya conectada a la autoridad HTTPS. Al implementar la primera utilidad habrá que añadir su lector y su operación tipada a la autoridad vigente; evitar una segunda configuración de conexión.

La actividad de negocio tampoco está implementada. Si se necesita, acordar qué eventos registrar (usuario, utilidad, instante, resultado y duración), su conservación y quién los verá. No registrar por defecto las filas completas, contraseñas ni SQL con datos personales.

## Definir antes de programar

1. Problema que resuelve y ejemplo concreto del resultado esperado.
2. Quién la usará y qué perfiles/usuarios tendrán permiso.
3. Datos de NixFarma necesarios, campos, relaciones y versión/esquema real. No inferir tablas por su nombre.
4. Filtros iniciales, columnas, orden, volumen habitual y máximo razonable.
5. Qué se hace con el resultado: consulta, detalle, impresión o exportación.
6. Criterio de aceptación con un caso conocido de la farmacia.

Recomendación: empezar por una consulta de solo lectura, acotada y útil en el trabajo diario. Cualquier modificación de NixFarma se define y autoriza por separado para esa utilidad.
