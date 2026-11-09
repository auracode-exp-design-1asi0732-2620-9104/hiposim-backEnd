# language: es
@TS03
Característica: Reporte PDF de una simulación
  Como Comprador
  Quiero descargar mi simulación como un documento PDF
  Para guardarla o compartirla fuera de la aplicación

  @TS03-200 @implemented
  Escenario: Descarga de una simulación sin cuenta
    Dado que el Comprador calculó una simulación sin haber iniciado sesión
    Cuando solicita el reporte con el código de esa simulación
    Entonces el sistema responde con código 200
    Y entrega un archivo application/pdf llamado hiposim-simulacion-{código}.pdf

  @TS03-200 @implemented
  Escenario: Descarga de una simulación propia
    Dado que el Comprador inició sesión y calculó una simulación
    Cuando solicita el reporte de esa simulación con su sesión
    Entonces el sistema responde con código 200
    Y entrega un archivo application/pdf

  @TS03-403 @implemented
  Escenario: Simulación de otro Comprador
    Dado que una simulación pertenece a un Comprador
    Cuando otro Comprador solicita su reporte
    Entonces el sistema responde con código 403
    Y no entrega ningún PDF

  @TS03-403 @implemented
  Escenario: Simulación de un Comprador consultada sin sesión
    Dado que una simulación pertenece a un Comprador
    Cuando se solicita su reporte sin iniciar sesión
    Entonces el sistema responde con código 403
    Y no entrega ningún PDF

  @TS03-404 @implemented
  Escenario: Simulación inexistente
    Dado que el código de simulación no existe
    Cuando se solicita su reporte
    Entonces el sistema responde con código 404
    Y no entrega ningún PDF

  @TS03-404 @implemented
  Escenario: Código con formato inválido
    Dado que el código de la solicitud no tiene formato de identificador
    Cuando se solicita el reporte
    Entonces el sistema responde con código 404

  @TS03 @implemented
  Escenario: Contenido del reporte
    Dado que se calculó una simulación de 240 meses con periodo de gracia
    Cuando se genera el reporte
    Entonces el PDF muestra los datos de la simulación, los resultados y los supuestos del cálculo
    Y muestra el cronograma completo con una fila por cada cuota, hasta el mes 240 con saldo 0
    Y marca con sombreado los meses de gracia
    Y indica que es un documento referencial
