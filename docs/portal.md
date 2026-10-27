# Centro de accesos Viteka · 0.10.1

Diseño adaptado de Viteka_Portal_Estudio_2026.html aportado por el usuario: fondo marfil, héroe editorial centrado, bloque prioritario petróleo, tarjetas claras de recursos, temas claro/oscuro/automático y contactos. El lenguaje visual se aplica también a estados vacíos y configuración. Se elimina el distintivo «MVP visual».

## Ventana y navegación

Controles nativos explícitos de minimizar, maximizar/restaurar y cerrar, con redimensionado. El menú puede plegarse a una columna de iconos y desplegarse manualmente. Se conserva el estado por usuario. El logotipo sin eslogan se mueve al pie; se elimina el texto duplicado «Farmacias vivas / Un espacio para tu farmacia». El isotipo bicolor original, con alfa transparente, se incluye en recursos WPF, icono del ejecutable y del instalador.

## Accesos del HTML

| Identificador | Destino o acción |
| --- | --- |
| support | https://soporte.viteka.es/ |
| manuals | https://wiki.nixfarma.es/ |
| training | https://www.youtube.com/@viteka_formacion |
| website | https://viteka.es/ |
| phone | tel:+34951552730 |
| support-email | mailto:soporte@viteka.es |
| info-email | mailto:info@viteka.es |
| anydesk | Abre AnyDesk.exe en Program Files (x86) o Program Files, sin argumentos de conexión |
| anydesk-download | Abre la página oficial de descarga, sin descargar ni ejecutar el cliente automáticamente |

Los enlaces se abren en la aplicación predeterminada de Windows. No se incrusta el portal externo dentro del WebView privilegiado, no se gestionan contraseñas ni se asume sesión iniciada. Copiar correo solo escribe la dirección aprobada en el portapapeles; abrir correo no envía mensajes.

Si AnyDesk no está en esas ubicaciones se muestra ayuda: puede estar en otra ruta y abrirse manualmente. No se ejecutan comandos remotos, conexiones automáticas ni configuraciones de acceso desatendido. No hace falta instalar el protocolo viteka-open del HTML ni ejecutar su VBS.

El horario mostrado es el habitual incluido en el archivo (lunes-viernes, 09:00–14:00 y 16:00–19:00); no se afirma disponibilidad en tiempo real ni se conecta el feed de excepciones/anuncios del HTML. Las actualizaciones del programa siguen usando GitHub como se acordó.

## Preferencias y validación

El esquema local mantiene compatibilidad con versiones anteriores y añade SidebarCollapsed/Theme con valores por defecto. El modo automático sigue el sistema; el anfitrión .NET adapta también la barra de título.

Las pruebas del núcleo validan persistencia, migración y lista cerrada de recursos. Las del navegador validan portal, menú, temas, guías, mensajes de recursos/portapapeles y actualizador. El smoke Windows comprueba además maximizar/restaurar, icono y guardado del menú. Los tests no abren AnyDesk, no envían mensajes ni inician sesiones remotas. La revisión de los enlaces con sesión de usuario y de AnyDesk en su equipo queda para la prueba real.

## Correcciones de integración Windows (0.9.0)

La barra de tareas y el área inicial se respetan usando el monitor real, coordenadas físicas, WM_GETMINMAXINFO y un manifiesto PerMonitorV2; la ventana normal se limita al área disponible incluso cuando el tamaño lógico inicial excede la pantalla por DPI. No se modifica la configuración de la barra de tareas de Windows.

La identidad de proceso y acceso directo es Viteka.Utilidades. Se aplica WM_SETICON para tamaños grande y pequeño desde el ICO transparente distribuido, además del recurso embebido. El instalador declara el icono y AppUserModelID del acceso directo. No se borra la caché global de iconos del usuario; un pin antiguo puede requerir desanclar y volver a anclar.

El zoom real del WebView2 se controla mediante Ctrl+rueda, teclado y barra nativa con porcentaje visible. No se aplica zoom CSS en Windows; solo la vista web usa zoom CSS para revisar el comportamiento. La escala admitida y persistente es 50–200 %. El puente admite window.zoom con validación numérica, nunca comandos de sistema arbitrarios.

## Espacio y apariencia (0.10.1)

La apariencia se elige en Configuración → Tu espacio de trabajo y conserva Automático/Claro/Oscuro por usuario. Inicio se centra en soporte y recursos: se elimina la franja superior de contexto y tema, se compactan cabecera, navegación, tarjetas y contactos, y se organizan las preferencias y actualizaciones en dos columnas cuando hay espacio. En tamaños pequeños se apilan y el menú tiene desplazamiento propio si lo necesita. En oscuro, tanto el logotipo como el isotipo del menú plegado se muestran en blanco, conservando su transparencia; en claro conservan los colores originales. Los iconos de Windows mantienen el isotipo bicolor.

Siguiente etapa acordada: primera prueba Oracle en el propio servidor, donde existe el DSN NIXFARMA. El acceso será de solo lectura y no se modificará el DSN compartido con NixFarma. La cuenta, sus permisos y su uso bajo la identidad real del servicio deben verificarse localmente. No solicitar contraseñas en chat ni distribuirlas en la carpeta compartida. La primera comprobación de conexión no implica que el servicio LAN, TLS, autorización de puestos o utilidades estén implementados.
