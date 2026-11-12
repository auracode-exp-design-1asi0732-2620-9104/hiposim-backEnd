# language: es
@EP01 @EP02 @EP06
Característica: Simulación de crédito hipotecario
  Como Comprador
  Quiero simular un crédito con el precio de la vivienda y la cuota inicial
  Para ver mi cuota mensual sin tener que registrarme primero

  @US01-E1 @TS06-E1 @implemented
  Escenario: Simulación exitosa
    Dado que el Comprador ingresa un valor de vivienda de 280000 y una cuota inicial de 42000
    Y que ingresa una tasa efectiva anual de 0.085 y un plazo de 240 meses
    Cuando solicita la simulación
    Entonces el sistema responde con código 200
    Y genera el cronograma de pagos bajo el método francés
    Y muestra la cuota mensual, el TCEA, el VAN y la TIR

  @US01-E2 @TS06-E2 @implemented
  Escenario: Cuota inicial insuficiente
    Dado que el Comprador ingresa un valor de vivienda de 280000 y una cuota inicial de 27999
    Cuando solicita la simulación
    Entonces el sistema responde con código 400
    Y informa que la cuota inicial mínima es del 10%
    Y no registra la simulación

  @TS06-E3 @implemented
  Escenario: Datos incompletos
    Dado que la solicitud no incluye los campos obligatorios
    Cuando la API procesa la solicitud
    Entonces responde con código 400
    Y la lista de campos con error incluye propertyPrice, downPayment, annualEffectiveRate y termInMonths

  @US01-E3 @implemented
  Escenario: Simulación sin cuenta
    Dado que el Comprador no ha iniciado sesión
    Cuando solicita la simulación
    Entonces el sistema muestra el resultado sin solicitar datos personales

  @US02-E1 @implemented
  Escenario: Cronograma detallado
    Dado que se completó una simulación
    Cuando el Comprador solicita el detalle del cronograma
    Entonces el sistema muestra, por cada mes, el saldo inicial, la amortización, el interés y el saldo final

  @US04-E1 @implemented
  Escenario: Indicadores calculados
    Dado que la simulación es exitosa
    Cuando el sistema presenta los resultados
    Entonces se muestran el TCEA, calculado con el seguro de desgravamen y los seguros estimados, junto con el VAN y la TIR

  @US05-E1 @implemented
  Escenario: Bono aplicado
    Dado que el valor de la vivienda de 280000 está dentro del rango vigente del bono
    Cuando el Comprador solicita aplicar el bono
    Entonces el sistema descuenta el subsidio de 18200 del monto a financiar
    Y el monto a financiar es 219800
    Y recalcula la cuota mensual en 1863.99

  @US05-E2 @implemented
  Escenario: Vivienda fuera de rango
    Dado que el valor de la vivienda de 450000 está fuera del rango vigente del bono
    Cuando el Comprador solicita aplicar el bono
    Entonces el sistema informa que la vivienda no califica
    Y no modifica el monto a financiar de 360000

  @US03-E1 @implemented
  Escenario: Periodo de gracia parcial
    Dado que el Comprador ingresa un valor de vivienda de 280000, una cuota inicial de 42000, una tasa efectiva anual de 0.0885 y un plazo de 240 meses
    Y que solicita el bono, por lo que el monto a financiar es 219800
    Y que elige un periodo de gracia parcial de 3 meses
    Cuando solicita la simulación
    Entonces el sistema responde con código 200
    Y en los 3 primeros meses el cronograma muestra solo el pago del interés, sin amortización y con el saldo sin cambios
    Y esos 3 meses quedan marcados como periodo de gracia
    Y desde el mes 4 la cuota se recalcula sobre los 237 meses restantes
    Y el saldo final del mes 240 es 0

  @US03-E2 @implemented
  Escenario: Periodo de gracia total
    Dado que el Comprador ingresa un valor de vivienda de 280000, una cuota inicial de 42000, una tasa efectiva anual de 0.0885 y un plazo de 240 meses
    Y que solicita el bono, por lo que el monto a financiar es 219800
    Y que elige un periodo de gracia total de 3 meses
    Cuando solicita la simulación
    Entonces el sistema responde con código 200
    Y en los 3 primeros meses el cronograma no muestra pagos y el interés se suma al saldo
    Y esos 3 meses quedan marcados como periodo de gracia
    Y desde el mes 4 la cuota se calcula sobre el saldo con los intereses capitalizados
    Y todas las cuotas regulares son iguales y la última no es una cuota excesiva
    Y el saldo final del mes 240 es 0

  @US03 @implemented
  Escenario: Periodo de gracia inconsistente
    Dado que el Comprador elige un tipo de gracia sin meses, meses de gracia sin tipo, un tipo desconocido, meses negativos o una gracia igual al plazo
    Cuando solicita la simulación
    Entonces el sistema responde con código 400
    Y informa el campo con error, gracePeriodMonths o graceType
    Y no registra la simulación

  @US03 @implemented
  Escenario: Gracia guardada con la simulación
    Dado que el Comprador solicita una simulación con gracia parcial o total de 3 meses
    Cuando el sistema la registra
    Entonces la simulación guardada conserva el tipo y los meses de gracia
    Y conserva los 3 meses marcados en el cronograma