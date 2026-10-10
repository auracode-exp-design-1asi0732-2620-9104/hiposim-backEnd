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