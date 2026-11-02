# Inventario y propuesta de identidad de Viteka Conecta

Fecha de revisión: 2 de noviembre de 2026. Documento técnico de trabajo; no es una condición contractual ni una política aprobada.

## Diagnóstico

La marca anterior aparece en superficies visibles y en identificadores técnicos persistentes. El cambio visible puede hacerse sin crear otra aplicación siempre que se conserven `AppId`, nombres de ejecutables y ensamblados, servicios, carpetas de datos, puertos, certificados existentes, manifiestos y nombres de artefactos que consume el actualizador.

El instalador actual ya distingue instalación limpia y actualización. Una actualización omite las páginas del asistente y puede reabrir el cliente con `/RELAUNCH=1`. El servicio se detiene antes de sustituir archivos y se vuelve a instalar o iniciar después. El cambio de marca no debe alterar ese flujo.

Contradicciones o puntos que no deben presentarse aún como funciones terminadas:

- No existe un control completo y comprobado de suscripción que, al vencer, deje únicamente Inicio y enlaces legales.
- El instalador no identifica a la persona, su rol ni la condición contractual particular; aceptar una licencia en el asistente no demostraría por sí solo la contratación, el precio ni la representación de la farmacia.
- No hay versión documental ni fecha de publicación aprobadas para las condiciones facilitadas. No se incorporan como documento definitivo hasta recibir esos datos y aprobación.
- No existe una política de privacidad final aprobada. No debe publicarse un enlace vacío ni una declaración absoluta sobre datos locales, telemetría o transferencias.
- La instalación inicial todavía usa credenciales de arranque conocidas y obliga a cambiarlas. Conviene sustituirlo por un alta inicial segura independiente del cambio de marca.
- La actualización conserva los almacenes existentes por diseño, pero la copia de seguridad es responsabilidad operativa y necesita prueba específica en Windows antes de producción.

## Cambios visibles incluidos en la propuesta

- Instalador, desinstalador, accesos directos y lista de aplicaciones: **Viteka Conecta**.
- Descriptor: **Herramientas y servicios para tu farmacia**.
- Ventana, acceso, Acerca de, actualizador, informes nuevos y metadatos visibles.
- Proveedor visible: Dinaro Partners, S.L.; NIF B72798168; domicilio y contacto facilitados.
- Documento instalado de información de uso y asistencia sin contraseñas iniciales ni versión fija.
- Mensajes de errores del instalador con acciones claras y contacto de asistencia.
- Las compilaciones de revisión mantienen la indicación visible **Pruebas**.

## Identificadores conservados

| Identificador | Decisión | Motivo |
|---|---|---|
| `AppId` de producción y revisión | Conservar | Evita una segunda instalación y mantiene la entrada de actualización/desinstalación. |
| `Viteka.Desktop.exe`, `Viteka.Server.exe` | Conservar | Los invocan instalador, servicio y actualizador. |
| Namespaces y ensamblados `Viteka.*` | Conservar | No son textos de marca y cambiarlos exigiría migración. |
| Servicios `VitekaUtilidadesAccess` y `VitekaUtilidadesReviewAccess` | Conservar | Mantiene detección, arranque, permisos y actualización. Solo cambia su nombre descriptivo visible. |
| Carpetas `Utilidades*` y datos de `ProgramData` | Conservar | Protege cuentas, configuración, credenciales y estados guardados. |
| `Viteka.Utilidades` como AppUserModelID | Conservar | Mantiene identidad del shell y accesos existentes. |
| Puertos 7447 y 7448 | Conservar | Evita romper puestos y reglas de red. |
| Autoridad local de certificados existente | Conservar | El nombre interviene en la confianza de los puestos; cambiarlo requiere migración separada. |
| Nombres actuales de instaladores y canal de actualización | Conservar inicialmente | Las versiones anteriores deben poder localizar, verificar y ejecutar las nuevas publicaciones. |

## Pruebas necesarias antes de publicar

1. Instalación limpia de producción y revisión en Windows.
2. Actualización sobre una instalación denominada Viteka Utilidades, incluida ejecución silenciosa y `/RELAUNCH=1`.
3. Conservación de usuarios, permisos, credenciales Oracle, configuración, cotejos, adjuntos y preferencias.
4. Acceso desde un puesto ya configurado y validez del certificado.
5. Detención, reinstalación y reinicio del servicio; fallos controlados de servicio y WebView2.
6. Desinstalación y ausencia de una segunda entrada de producto.
7. Accesos directos nuevos sin borrar accesos personales; revisión de fijaciones antiguas.
8. Tildes, versión real y metadatos en instalador, ejecutable, Acerca de y exportaciones nuevas.
9. Licencia sin aceptación premarcada cuando exista una versión documental aprobada.

## Cambios que requieren aprobación adicional

- Versión y fecha de publicación de las condiciones de contratación, licencia y uso.
- Procedimiento de aceptación contractual y datos que se registrarán.
- Política de privacidad completa.
- Flujo de vencimiento de suscripción.
- Sustitución del alta inicial basada en credenciales conocidas.
- Cualquier cambio de identificador técnico, ruta, servicio, certificado o nombre de artefacto.
