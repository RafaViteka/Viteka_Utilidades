Actualización desatendida con sesión, nueva escala de diseño y código formateado.

- Actualizar ahora instala la nueva versión sin asistente ni preguntas: solo queda la confirmación de permisos de Windows (UAC). Al terminar, Viteka se vuelve a abrir sola con la sesión iniciada, sin pedir la contraseña. Si la instalación falla, se reabre la versión anterior.
- La sesión se guarda cifrada (DPAPI del usuario), sirve una sola vez y caduca a los 15 minutos. Si se cancela el aviso de Windows, se descarta.
- El servicio de acceso conserva las sesiones al reiniciarse (solo guarda el hash de cada token, protegido con DPAPI de máquina). Los puestos ya no tienen que volver a entrar tras una actualización.
- Diseño: textos con un mínimo de 12 px y una escala única de tamaños, colores unificados en variables (con avisos adaptados al modo oscuro) y fuente Inter incluida en la aplicación.
- Tenencia: los nombres de grupo ocupan dos líneas como máximo (el nombre completo aparece al pasar el ratón) y caben más grupos en pantalla. La leyenda de cubiertos, faltas y pendientes se muestra en etiquetas de color.
- Inicio: cabecera más compacta.
- Código de la interfaz y del servicio formateado para facilitar su mantenimiento. Sin cambios en las reglas de tenencia ni en la conexión con NixFarma.

Esta actualización (desde 0.14.x) todavía se instala con el procedimiento anterior: desde Configuración → Actualizaciones en el servidor, con los clientes cerrados, sin desinstalar. La instalación desatendida con reapertura y sesión se aplica a partir de las siguientes versiones. Se conservan usuarios, conexión y datos de farmacia.
