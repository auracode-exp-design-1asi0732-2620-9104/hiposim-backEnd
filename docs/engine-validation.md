# **Financial Engine Validation and Testing Suite Evidence**

<div style="text-align: justify; line-height: 1.6;">

Este documento reúne la evidencia de la historia **SP02 (Validar el port del motor financiero a C#)** y de las pruebas automatizadas del motor financiero y del endpoint de simulación (**TS06**). La Parte 1 documenta la validación del port frente a AutoFinance Pro: diferencia máxima observada, reglas de redondeo y prototipo mínimo del cálculo. La Parte 2 documenta la suite de pruebas: qué se prueba, con qué herramientas, con qué resultados y cómo reproducirlo. Todas las cifras provienen de ejecuciones reales del código; lo que aún no existe se marca como pendiente.

</div>

| Campo | Detalle |
|:--|:--|
| Historias relacionadas | SP02, TS06 (y los escenarios de motor de US04 y US05) |
| Responsable | Franco López |
| Fecha de ejecución de las pruebas | 2026-10-03 |
| Repositorio | *Pendiente: URL de `hiposim-platform` cuando exista.* |
| Commit o Pull Request | *Pendiente.* |

---

# **Part 1. Engine Validation (SP02)**

## **1.1. Objective and Scope**

<div style="text-align: justify; line-height: 1.6;">

El criterio de aceptación de SP02 exige que, al ejecutar los casos de prueba documentados de AutoFinance Pro contra la implementación en C#, se documenten la diferencia máxima observada, las reglas de redondeo y un prototipo mínimo del cálculo. El alcance de la validación es la **equivalencia numérica del port**: método francés, periodos de gracia total y parcial, cuota balón opcional, conversión de tasas, VAN, TIR y TCEA. No incluye la validación frente a simuladores de bancos ni la corrección regulatoria del TCEA con comisiones (ver la sección 1.7).

</div>

## **1.2. Reference Implementation and Method**

<div style="text-align: justify; line-height: 1.6;">

La **implementación de referencia** es el motor Python de AutoFinance Pro, ejecutado a través de `CreditoService.simular`, es decir, con el mismo flujo completo que usaba la aplicación original (conversión de tasa, cronograma, totales e indicadores). La **implementación evaluada** es la biblioteca `HipoSim.FinancialEngine` en C#.

</div>

| AutoFinance Pro (Python) | HipoSim (C#) |
|:--|:--|
| `tasas_service.py` | `RateConverter` |
| `amortizacion_service.py` (`cuota_francesa`, `generar_cronograma`) | `AmortizationCalculator` |
| `indicadores_service.py` | `FinancialIndicators` |
| `credito_service.py` (`simular`) | `FinancialEngine.Simulate` |
| Ninguno (el bono no existía) | `BenefitsEngine` |

<div style="text-align: justify; line-height: 1.6;">

El método tiene cuatro pasos. Primero, el script `tools/generate_parity_cases.py` ejecuta el motor Python original sobre 48 escenarios y guarda las entradas y los resultados esperados en `GoldenData/python-parity-cases.json`. Segundo, la prueba `PythonParityTests` ejecuta los mismos escenarios en C#, partiendo de exactamente los mismos números (las tasas se guardan como la fracción que calcula Python). Tercero, compara cada fila del cronograma y cada indicador. Cuarto, falla si algún monto difiere en más de **S/ 0.01** o alguna tasa en más de **0.000001**, o si cambia el número de cuota, la fecha de pago o la marca de periodo de gracia.

</div>

**Scenarios covered (48)**

| Categoría | Escenarios | Qué cubre |
|:--|--:|:--|
| Casos del informe de AutoFinance Pro | 3 | Estándar con cuota balón, gracia parcial, gracia total con tasa nominal. |
| Ejemplo del contrato de API | 2 | S/ 280,000, 15% de cuota inicial, TEA 8.5%, 240 meses, con y sin bono. |
| Malla plazo x tasa x gracia | 24 | Plazos de 60, 180, 240 y 360 meses; tasas de 7.0% y 9.5%; sin gracia, gracia parcial de 6 meses y gracia total de 12 meses; seguros, gastos y tasas de descuento variables. |
| Tasa nominal | 7 | Una por cada frecuencia de capitalización (diaria a anual). |
| Tramos del Bono del Buen Pagador | 4 | Viviendas de S/ 180,000, 250,000, 380,000 y 450,000. |
| Cuota balón | 3 | 10%, 20% con gracia parcial y 40%. |
| Fechas de fin de mes | 3 | Inicio el 31 de enero, el 29 de febrero (año bisiesto) y el 31 de diciembre con gracia total. |
| Casos extremos | 2 | Tasa cero y tasa alta (18.5%). |

<div style="text-align: justify; line-height: 1.6;">

Los plazos cubiertos son 24, 36, 48, 60, 72, 120, 180, 240, 300 y 360 meses.

</div>

## **1.3. Results**

### **1.3.1. AutoFinance Pro Documented Cases**

<div style="text-align: justify; line-height: 1.6;">

Las cifras de referencia son las registradas en las pruebas de AutoFinance Pro (`test_amortizacion_service.py`), que las toman del informe original (sección 7). Las pruebas originales aceptaban una tolerancia de hasta S/ 1.00 por el redondeo de las capturas del informe; el port en C# coincide **al centavo** en todas las cifras disponibles.

</div>

| Caso | Cifra | Informe | C# | Diferencia |
|:--|:--|--:|--:|--:|
| 1. S/ 68,000, TEA 10.7%, 60 meses, balón 25% | Cuota regular | 1,088.84 | 1,088.84 | 0.00 |
| | Interés de la cuota 1 | 578.48 | 578.48 | 0.00 |
| | Amortización de la cuota 1 | 510.36 | 510.36 | 0.00 |
| | Saldo después de la cuota 1 | 67,489.64 | 67,489.64 | 0.00 |
| 2. S/ 102,000, TEA 11.8%, 72 meses, gracia parcial de 3 meses, balón 30% | Interés y cuota de cada mes de gracia | 952.52 | 952.52 | 0.00 |
| | Saldo durante la gracia | 102,000.00 | 102,000.00 | 0.00 |
| | Interés de la primera cuota regular | 952.52 | 952.52 | 0.00 |
| | Amortización de la primera cuota regular | 455.86 | 455.86 | 0.00 |
| | Primera cuota regular | 1,408.38 | 1,408.38 | 0.00 |

<div style="text-align: justify; line-height: 1.6;">

El caso 3 (S/ 24,000, TNA 12% con capitalización mensual, 48 meses, gracia total de 2 meses, balón 20%) no tiene cifras puntuales en el material disponible; su referencia es cualitativa: el saldo debe crecer durante la gracia por capitalización de intereses y el crédito debe terminar en cero. El C# cumple ambas condiciones. Además, el resultado es verificable a mano: una TNA de 12% con capitalización mensual equivale a 1% mensual, de modo que el interés del primer mes es S/ 240.00 (24,000 x 1%), el saldo pasa a S/ 24,240.00 y luego a S/ 24,482.40, que son los valores que produce el motor. La primera cuota regular es S/ 522.77.

</div>

### **1.3.2. Parity Against the Python Engine**

| Concepto | Valor |
|:--|--:|
| Escenarios ejecutados | 48 |
| Filas de cronograma comparadas | 9,156 |
| Valores numéricos comparados (7 por fila más 10 del resumen por escenario) | 64,572 |
| Comprobaciones estructurales (número de cuota, fecha de pago, marcas de gracia y de balón) | 36,624 |
| **Diferencia máxima observada en montos** | **0.00** |
| **Diferencia máxima observada en tasas** | **0** |
| Valores que difieren aunque sea en el último decimal | 0 |
| Escenarios cuyo saldo final es 0.00 | 48 de 48 |

<div style="text-align: justify; line-height: 1.6;">

Los valores comparados son cuota, interés, amortización, saldo, seguros y pago total de cada fila, y el resumen: monto financiado, cuota regular, cuota balón, total pagado, total de intereses, costos adicionales, VAN, TIR mensual, TIR anual y TCEA. Como ningún valor difiere, el umbral de aceptación (S/ 0.01 y 0.000001) se cumple con margen completo.

</div>

### **1.3.3. Internal Consistency**

<div style="text-align: justify; line-height: 1.6;">

Además de la comparación con Python, se verificaron propiedades que no dependen de él: sin seguros y sin cuota balón, el TCEA es igual a la TEA pactada (a 4 decimales); con seguros, el TCEA es mayor que la tasa; una tasa nominal de 12% con capitalización mensual equivale a una TEA de 12.6825%; con tasa cero la cuota reparte el capital en partes iguales; y el crédito siempre termina con saldo cero. La suma de totales de las filas coincide con el total pagado (diferencia observada 0.00), y la suma de las amortizaciones redondeadas difiere del monto financiado en un máximo de S/ 0.10 (plazo de 360 cuotas con gracia total, contando los intereses capitalizados), por el redondeo de cada fila a centavos.

</div>

## **1.4. Rounding Rules**

| Valor | Regla |
|:--|:--|
| Montos de salida (cuota, interés, amortización, saldo, pago total, cuota balón, resumen, VAN) | 2 decimales con redondeo bancario (mitad al par), el mismo comportamiento de `round()` en Python. |
| Tasas de salida (TIR mensual, TIR anual, TCEA) | 6 decimales con el mismo redondeo. |
| Cálculo interno | Doble precisión (`double`) sin redondeos intermedios: el saldo y los intereses se arrastran sin redondear y solo se redondea lo que se emite en cada fila. |
| Monto financiado | `precio - cuota inicial - bono`, redondeado a 2 decimales. |
| Cuota balón | Se redondea a 2 decimales antes de sumarse a la última cuota, como en Python. |
| Última cuota | Liquida el saldo remanente, de modo que el saldo final es exactamente 0.00. |
| Seguros | Se emiten tal como se configuran (sin redondeo adicional) y se suman a cada pago total. |
| Calendario comercial | Año de 360 días y mes de 30 días: `TEP = (1 + TEA)^(30/360) - 1`. |
| Fechas de pago | Se suma un mes a la fecha anterior de forma acumulativa, conservando el día recortado (31 de enero, 28 de febrero, 28 de marzo). Coincide con `relativedelta` de Python. |
| Cero negativo | Un resultado como `-0.00` se normaliza a `0.00`. |

<div style="text-align: justify; line-height: 1.6;">

La equivalencia del redondeo entre `Math.Round` de .NET y `round()` de Python se comprobó empíricamente: ninguno de los 64,572 valores numéricos comparados difiere. Podrían existir diferencias teóricas en valores que caen exactamente en un punto medio de la representación binaria; no se observó ninguna.

</div>

## **1.5. Documented Deviations and Observations**

**Intentional deviations from the Python engine**

| ID | Desviación | Motivo |
|:--|:--|:--|
| D1 | La última cuota se marca como cuota balón solo si el porcentaje de balón es mayor que 0. | Python la marcaba siempre, aun sin balón. En una hipoteca sin balón sería información incorrecta. |
| D2 | Se normaliza el cero negativo. | Python emitió `-0.0` como monto de cuota balón en uno de los escenarios; en JSON se vería como `-0`. |
| D3 | Las tasas se expresan como fracciones (0.085), no como porcentajes (8.5). | Coincide con el contrato de API. La prueba de paridad parte del mismo número que usa Python. |
| D4 | El motor no limita el plazo (Python lo limitaba a 96 meses por ser de vehículos); lanza `ArgumentException` ante entradas inconsistentes. | Los topes del producto (60 a 360 meses) los valida el endpoint, no el motor. |

**Observations inherited from AutoFinance Pro (decisions pending for the team)**

| ID | Observación |
|:--|:--|
| O1 | El TCEA se calcula con el flujo de cuotas más seguros, pero **no incluye los gastos administrativos**, aunque estos sí se suman a los costos adicionales. Es el comportamiento del motor original. Si el TCEA hipotecario debe incluirlos, es un cambio funcional que requiere una decisión del equipo. |
| O2 | El VAN se calcula desde la óptica del prestamista y descontado a una tasa del 10% anual. Por eso es negativo cuando la tasa del crédito es menor que la de descuento (por ejemplo, S/ -20,791.75 en el ejemplo del contrato). Falta definir cómo se explica al comprador. |
| O3 | Los seguros son montos mensuales fijos, no un porcentaje del saldo. En el endpoint su valor por defecto es 0 hasta definirlos, de modo que hoy el TCEA resulta igual a la tasa. |
| O4 | La tabla del Bono del Buen Pagador (hasta S/ 200,000: S/ 26,400; hasta 300,000: S/ 18,200; hasta 400,000: S/ 10,000) es **referencial**, copiada del simulador del Landing Page. No es una tabla oficial del Fondo Mivivienda. |

## **1.6. Minimal Prototype**

<div style="text-align: justify; line-height: 1.6;">

El prototipo mínimo es una aplicación de consola (`samples/HipoSim.EnginePrototype`) que calcula el ejemplo del contrato de API: vivienda de S/ 280,000, cuota inicial de S/ 42,000, TEA de 8.5%, 20 años y Bono del Buen Pagador solicitado.

</div>

```csharp
var bonus = new BenefitsEngine().EvaluateGoodPayerBonus(propertyPrice: 280_000, apply: true);

var result = new FinancialEngine().Simulate(new LoanSimulationInput(
    PropertyPrice: 280_000,
    DownPayment: 42_000,
    RateType: RateType.Effective,
    AnnualRate: 0.085,
    TermInMonths: 240,
    StartDate: new DateOnly(2026, 10, 1),
    BenefitAmount: bonus.AppliedAmount));
```

Salida real de `dotnet run --project samples/HipoSim.EnginePrototype`:

```text
Good Payer Bonus: eligible=True, applied=18,200.00
Financed amount         219,800.00
Monthly installment       1,863.99
Total paid              447,357.60
Total interest          227,557.97
TCEA                      8.5000 %
NPV (10% discount)      -20,791.75

Period  Payment date  Installment    Interest  Amortization       Balance
     1  2026-11-01     1,863.99    1,499.36        364.63    219,435.37
     2  2026-12-01     1,863.99    1,496.88        367.11    219,068.26
     3  2027-01-01     1,863.99    1,494.37        369.62    218,698.64
   240  2046-10-01     1,863.99       12.63      1,851.36          0.00
```

## **1.7. Limitations**

- La referencia es el propio motor de AutoFinance Pro y las cifras de su informe. Si el original tuviera un error conceptual, el port lo reproduciría. Para mitigarlo se verificaron propiedades independientes (sección 1.3.3), pero **no se ha contrastado con simuladores externos** (BCP, Interbank, Fondo Mivivienda).
- No se valida el TCEA con comisiones, portes ni seguros calculados como porcentaje del saldo, porque el motor no los modela (observaciones O1 y O3).
- La tabla del bono es referencial (observación O4).
- La persistencia de simulaciones no está probada: solo existe la interfaz `ISimulationRepository` y las pruebas usan un repositorio en memoria. Tampoco hay pruebas contra PostgreSQL ni con un servidor JWT real.
- No se realizaron pruebas de rendimiento, carga ni seguridad.
- Los datos de paridad se generaron con Python 3.11.7. Desde Python 3.12 la función `sum()` usa suma compensada para números decimales, así que regenerar los datos con una versión más reciente podría cambiar los últimos dígitos internos de los totales (no los valores redondeados, salvo casos extremos).

**Conclusion.** El port cumple el criterio de aceptación de SP02: con la misma entrada produce los mismos resultados que el motor original en los 48 escenarios y en los casos documentados del informe (diferencia máxima 0.00), las reglas de redondeo están documentadas y existe un prototipo ejecutable. Esto valida la **equivalencia con AutoFinance Pro**, no la exactitud del producto frente al mercado hipotecario.

---

# **Part 2. Testing Suite Evidence**

## **2.1. Tools and Environment**

| Elemento | Detalle |
|:--|:--|
| Lenguaje y plataforma | C# sobre .NET. Las bibliotecas apuntan a `net8.0`; los proyectos de prueba a `net9.0`. |
| SDK de .NET | 9.0.318 |
| Sistema operativo | Windows 11, versión 10.0.26200 |
| Marco de pruebas | xUnit 2.9.2, Microsoft.NET.Test.Sdk 17.12.0, xunit.runner.visualstudio 2.8.2 |
| Pruebas de integración HTTP | Microsoft.AspNetCore.TestHost 9.0.20 (servidor en memoria con el controlador real) |
| Prueba de contrato | YamlDotNet 18.1.0 (lee `docs/api-contract/openapi.v0.yaml`) |
| Cobertura | coverlet.collector 6.0.2 |
| Motor de referencia | Python 3.11.7 (AutoFinance Pro) |

## **2.2. Test Suites**

| Proyecto | Clase de prueba | Pruebas | Tipo | Qué verifica |
|:--|:--|--:|:--|:--|
| `HipoSim.FinancialEngine.Tests` | `RateConverterTests` | 6 | Unitaria | Conversión TNA a TEA a tasa mensual y errores de entrada. |
| | `AmortizationCalculatorTests` | 18 | Unitaria | Los 3 casos del informe, tasa cero, fechas de fin de mes, seguros, marca de balón y entradas inconsistentes. |
| | `FinancialIndicatorsTests` | 9 | Unitaria | VAN, TIR (incluida la bisección, la TIR negativa y la derivada cero) y propiedades del TCEA. |
| | `BenefitsEngineTests` | 11 | Unitaria | Tramos del bono en sus límites, fuera de rango, sin solicitarlo y tramos personalizados. |
| | `FinancialEngineTests` | 13 | Unitaria | Flujo completo del motor, monto financiado, gracia, tasa nominal y validaciones. |
| | `PythonParityTests` | 49 | Regresión / paridad | 48 escenarios contra el motor Python más un resumen de la diferencia máxima. |
| `HipoSim.Simulations.Tests` | `SimulationRequestValidatorTests` | 32 | Unitaria | Reglas de validación de TS06: campos obligatorios, 10% de cuota inicial, tasa como fracción, plazo y gracia. |
| | `SimulationServiceTests` | 10 | Unitaria | Servicio con repositorio y reloj simulados: bono, supuestos, fecha de inicio en Lima, saldo inicial de cada fila, persistencia. |
| | `SimulationsEndpointTests` | 12 | Integración | `POST /api/simulations` con el controlador real: 200, 400, JSON inválido, cuerpo vacío, comprador autenticado. |
| | `ContractConformanceTests` | 6 | Contrato | Los ejemplos del YAML se envían al endpoint y las respuestas deben coincidir con lo documentado. |
| **Total** | | **166** | | |

## **2.3. Execution Results**

Comandos ejecutados desde la carpeta de la solución:

```bash
dotnet test
dotnet test --collect:"XPlat Code Coverage" --logger "trx"
```

| Proyecto | Pruebas | Correctas | Con error | Omitidas | Duración |
|:--|--:|--:|--:|--:|--:|
| `HipoSim.FinancialEngine.Tests` | 106 | 106 | 0 | 0 | 216 ms |
| `HipoSim.Simulations.Tests` | 60 | 60 | 0 | 0 | 879 ms |
| **Total** | **166** | **166** | **0** | **0** | |

<div style="text-align: justify; line-height: 1.6;">

La compilación de toda la solución (cuatro proyectos de código y prueba más el prototipo) termina con 0 advertencias y 0 errores. *Pendiente: captura de pantalla de la salida de `dotnet test` o del Test Explorer.*

</div>

## **2.4. Code Coverage**

| Ensamblado | Suite que lo ejercita | Líneas | Ramas |
|:--|:--|--:|--:|
| `HipoSim.FinancialEngine` | `HipoSim.FinancialEngine.Tests` | 99.7% | 89.0% |
| `HipoSim.Simulations` | `HipoSim.Simulations.Tests` | 99.3% | 98.9% |

| Clase del motor | Líneas | Ramas |
|:--|--:|--:|
| `AmortizationCalculator` | 100.0% | 97.0% |
| `BenefitsEngine` | 100.0% | 100.0% |
| `FinancialEngine` | 100.0% | 85.0% |
| `FinancialIndicators` | 98.6% | 79.4% |
| `RateConverter` | 100.0% | 100.0% |

<div style="text-align: justify; line-height: 1.6;">

Las únicas líneas sin cubrir son tres: el `return` final de la bisección de la TIR (`FinancialIndicators`), prácticamente inalcanzable con el flujo de un crédito porque la bisección converge antes de agotar sus iteraciones, y el salto de entradas sin errores al construir los errores de enlace del controlador (`SimulationsController`, dos líneas). La cobertura de ramas del motor (89.0%) es menor que la de líneas; las ramas sin cubrir son combinaciones de condiciones dentro de líneas ya ejecutadas y no se analizaron una por una.

</div>

## **2.5. Sanity Checks by Mutation**

<div style="text-align: justify; line-height: 1.6;">

Para comprobar que las pruebas detectan errores y no solo pasan, se introdujeron cambios deliberados y temporales en el código, se ejecutó la suite y luego se restauró el código original (la suite completa volvió a pasar).

</div>

| Cambio introducido | Suite ejecutada | Resultado |
|:--|:--|:--|
| El interés de los meses de gracia se multiplica por 1.0001 | Paridad (49 pruebas) | 21 fallan |
| El exponente del descuento mensual del VAN pasa de 1/12 a 1/13 | Paridad (49 pruebas) | 49 fallan |
| La regla de cuota inicial pasa de 10% a 5% | `HipoSim.Simulations.Tests` (56 pruebas en ese momento) | 2 fallan |
| Se renombra la propiedad `Irr` de la respuesta en el código | `HipoSim.Simulations.Tests` | Error de compilación en las pruebas |
| Solo cambia el nombre JSON de `irr` a `irrAnnual` (el código compila igual) | `HipoSim.Simulations.Tests` (56 pruebas en ese momento) | 3 fallan (contrato) |

## **2.6. Traceability**

| Historia o escenario | Pruebas que lo verifican |
|:--|:--|
| SP02, escenario 1: resultados validados | `PythonParityTests` (49), los 3 casos del informe en `AmortizationCalculatorTests`, prototipo `HipoSim.EnginePrototype`. |
| TS06, escenario 1: simulación calculada (200) | `Post_WhenRequestIsValid_Returns200WithTheCalculation`, `ContractConformanceTests`. |
| TS06, escenario 2: cuota inicial menor al 10% (400) | `Post_WhenDownPaymentIsBelowTenPercent_Returns400AsProblemJson` y los límites de `SimulationRequestValidatorTests` (27,999 rechazado, 28,000 aceptado). |
| TS06, escenario 3: datos incompletos (400 con lista de campos) | `Post_WhenRequiredFieldsAreMissing_Returns400ListingEveryField`, `Post_WhenOnlySomeFieldsAreMissing_ListsOnlyThose`. |
| US05, escenario 1: bono aplicado | `BenefitsEngineTests`, `FinancialEngineTests`, `SimulationServiceTests`, ejemplo `withBonus` del contrato. |
| US05, escenario 2: vivienda fuera de rango | `BenefitsEngineTests`, `Post_WhenPropertyIsOutsideTheBonusRange_Returns200WithoutTheBonus`, ejemplo `outOfRange` del contrato. |
| US04, escenario 1: TCEA con seguros | `FinancialIndicatorsTests`, `SimulationServiceTests`, `Post_WhenTheConfigurationDefinesInsurance_TheAssumptionsAreEchoedAndUsed`. |
| US02, escenario 1: cronograma con saldo inicial, amortización, interés y saldo final (parte de API) | `CalculateAsync_WhenCalculated_EachRowOpensWithThePreviousClosingBalance`, `ContractConformanceTests` (campo `openingBalance`). |

<div style="text-align: justify; line-height: 1.6;">

Las pruebas cubren la parte de motor y API de US04 y US05. La pantalla del móvil de ambas historias se evidencia con las pruebas del repositorio de la aplicación móvil, no con esta suite. En US04, el valor por defecto de los seguros está pendiente (observación O3).

</div>

## **2.7. How to Reproduce**

```bash
# Run all tests
dotnet test

# Run only the parity tests and print the observed maximum difference
dotnet test tests/HipoSim.FinancialEngine.Tests --filter "FullyQualifiedName~ReportsTheMaximumObservedDifference" --logger "console;verbosity=detailed"

# Run the minimal prototype
dotnet run --project samples/HipoSim.EnginePrototype

# Regenerate the parity data with the original Python engine (needs its virtualenv)
<autofinance-pro-backend>\.venv\Scripts\python.exe tools\generate_parity_cases.py
```

La salida de la prueba de paridad incluye una línea de la forma `PARITY cases=48 rows=9156 maxMoneyDifference=0 maxRateDifference=0 differingValues=0`.

## **2.8. Pending Evidence**

| Evidencia | Estado |
|:--|:--|
| URL del repositorio, commit y Pull Request | *Pendiente: se completa cuando exista `hiposim-platform` y se integre este código.* |
| Ejecución en integración continua | *Pendiente: no hay flujo de CI configurado para el backend.* |
| Captura de pantalla de la ejecución de las pruebas | *Pendiente.* |
| Colección Postman del endpoint de simulación | *Pendiente (Services Documentation Evidence).* |
| Pruebas con persistencia real en PostgreSQL | *Pendiente: depende de la implementación del repositorio en la API.* |
