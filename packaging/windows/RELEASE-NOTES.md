# Viteka Conecta 0.18.25

Corrección del acceso desde puestos y de la incorporación de artículos.

- La regla HTTPS del servidor funciona en redes de Windows clasificadas como Dominio, Privada o Pública y continúa limitada a la subred local de la farmacia.
- El editor reconoce tanto `APADMIN_AB_ARTICULOS` como `APADMIN.AB_ARTICULOS`, las dos formas detectadas en instalaciones reales de Nixfarma.
- Se mantienen los destinos alternativos certificados y los mensajes específicos de conexión introducidos en la versión anterior.

La instalación conserva cuentas, configuración y datos al actualizar. El disco `C:` debe estar compartido por la farmacia de forma que los puestos vean la misma ruta como `X:`.
