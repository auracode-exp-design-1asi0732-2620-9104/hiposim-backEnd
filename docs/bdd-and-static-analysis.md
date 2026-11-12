# **BDD Scenarios and Static Analysis (Tasks 6.1 and 6.2)**

Documento de las tareas 6.1 y 6.2 del Sprint 3-4: escenarios BDD de las funciones nuevas (gracia y reporte PDF) y análisis estático de la solución. Todas las cifras de este documento se midieron el 2026-11-09 con el SDK de .NET 9.0.320.

## **1. BDD Acceptance Scenarios**

### **1.1. Where They Live and What They Are**

Los escenarios están en `tests/acceptance/features/` y se escriben en español (Gherkin):

| Archivo | Contenido | Cambio en esta tarea |
|:--|:--|:--|
| `simulation.feature` | Simulación de crédito: éxito, validaciones, cronograma, indicadores, bono. | +4 escenarios de gracia (US03). |
| `report.feature` | Reporte PDF de una simulación (TS03). | Archivo nuevo, 7 escenarios. |
| `leads.feature`, `registration.feature` | Conexión con inmobiliarias y registro. | Sin cambios. |

**Importante:** los archivos `.feature` no se ejecutan. El proyecto no usa Reqnroll ni SpecFlow, así que no existe un vínculo automático entre un escenario y una prueba. La etiqueta `@implemented` es una marca manual que significa "hay pruebas xUnit que verifican este comportamiento"; la tabla de la sección 1.2 dice cuáles. Si se cambia una prueba o se renombra, la tabla puede quedar desactualizada y hay que revisarla a mano.

Las etiquetas `@US03-E1` y `@US03-E2` siguen la numeración de escenarios que ya usaba la trazabilidad de `engine-validation.md` (sección 2.6). Los escenarios sin número (`@US03`, `@TS03`) son pruebas adicionales que no vienen de un escenario del Jira. `report.feature` no lleva etiqueta de épica porque no se confirmó cuál le corresponde.

### **1.2. Traceability of the New Scenarios**

Los nombres de las pruebas son los del repositorio. Las pruebas `GetReport_*` sin otra clase indicada están en `SimulationReportEndpointTests`.

| Escenario | Pruebas que lo verifican |
|:--|:--|
| US03-E1, gracia parcial | `GracePeriodEndpointTests.Post_WhenPartialGraceOfThreeMonths_PaysOnlyInterestThenRecalculatesTheInstallmentFromMonthFour`, `BuildSchedule_WhenPartialGrace_...` en `AmortizationCalculatorTests`, ejemplo `partialGrace` del contrato. |
| US03-E2, gracia total | `GracePeriodEndpointTests.Post_WhenTotalGraceOfThreeMonths_RecordsNoPaymentsAndCapitalizesTheInterest` y `Post_WhenTotalGrace_EveryRegularInstallmentIsEqualAndTheLastOneIsNotAHugeBalloon`, `BuildSchedule_WhenTotalGrace_...` (desviación D5), ejemplo `totalGrace` del contrato. |
| US03, gracia inconsistente | `Post_WhenTheGraceIsInconsistent_Returns400KeyedByTheFieldAndStoresNothing` (5 casos: sin meses, sin tipo, tipo desconocido, meses negativos y gracia igual al plazo). |
| US03, gracia guardada | `Post_WhenGraceIsRequested_TheStoredSimulationKeepsTheGraceInputAndTheMarkedSchedule` (parcial y total). |
| TS03-200, sin cuenta | `GetReport_WhenTheSimulationWasCalculatedWithoutSigningIn_Returns200WithAPdf` y, en `SimulationReportServiceTests`, `GetReport_WhenTheSimulationHasNoBuyer_AnyCallerReceivesThePdf`. |
| TS03-200, simulación propia | `GetReport_WhenTheOwnerAsksForTheirSimulation_Returns200WithAPdf`, `GetReport_WhenTheCallerIsTheOwner_ReturnsThePdf` y la prueba de extremo a extremo `Simulation_report_is_read_back_from_the_database_and_only_its_owner_can_download_it` (`HipoSim.Api.Tests`). |
| TS03-403, otro Comprador | `GetReport_WhenAnotherBuyerAsks_Returns403AsProblemJson` y, en el servicio, `GetReport_WhenTheSimulationBelongsToABuyerAndTheCallerIsNotThatBuyer_ReturnsForbidden`. |
| TS03-403, sin sesión | `GetReport_WhenNobodyIsSignedInAndTheSimulationBelongsToABuyer_Returns403`. |
| TS03-404, inexistente | `GetReport_WhenTheIdDoesNotExist_Returns404AsProblemJson` y, en el servicio, `GetReport_WhenTheIdIsUnknown_ReturnsNotFoundWithoutGeneratingAnything`. |
| TS03-404, formato inválido | `GetReport_WhenTheIdIsNotAGuid_Returns404`. |
| TS03, contenido del reporte | `SimulationReportContentBuilderTests` (16), `QuestPdfSimulationReportGeneratorTests` (5, incluidas las páginas con 60 y 240 cuotas), `GetReport_WhenTheSimulationHasGraceAndInsurance_Returns200` y 3 pruebas del PDF en `ContractConformanceTests`. |

Los escenarios anteriores de `simulation.feature` (US01, US02, US04, US05 y TS06) ya estaban trazados en la sección 2.6 de `engine-validation.md` y no se tocaron. Los escenarios de `leads.feature` y `registration.feature` pertenecen a otras tareas y no se revisaron aquí, por lo que su etiqueta `@implemented` no está comprobada por este documento.

## **2. Static Analysis**

### **2.1. What Was Analyzed**

El análisis se hizo sobre una copia temporal que junta `origin/develop` (`8dde64e`) con las tres ramas de los Pull Requests abiertos: `feature/us03-total-grace-fix` y `feature/us03-grace-api-tests` (punta `594f632`, incluye el arreglo) y `feature/ts03-simulation-report` (punta `ccc9de7`). El único conflicto al juntarlas fue `docs/engine-validation.md` (conteos de pruebas), que no afecta al código. La copia es temporal y no se subió a ningún repositorio.

Se analizaron los siete proyectos de la solución: motor, simulaciones, API, el prototipo y los tres proyectos de pruebas.

### **2.2. Tools and Commands**

| Herramienta | Comando | Qué revisa |
|:--|:--|:--|
| Compilador con analizadores por defecto | `dotnet build HipoSim.FinancialEngine.sln` | Advertencias del compilador y reglas activas por defecto. |
| Analizadores de .NET en modo Recommended | `dotnet build HipoSim.FinancialEngine.sln --no-incremental -p:AnalysisMode=Recommended -p:EnableNETAnalyzers=true` | Reglas de calidad CA (diseño, rendimiento, globalización). |
| `dotnet format` | `dotnet format HipoSim.FinancialEngine.sln --verify-no-changes --no-restore --severity info --report <carpeta>` | Espacios en blanco, estilo (IDE) y analizadores a nivel `info`. Termina con código 2 si algo no cumple. |
| Paquetes vulnerables | `dotnet list HipoSim.FinancialEngine.sln package --vulnerable --include-transitive` | Vulnerabilidades conocidas en paquetes directos y transitivos. |
| Paquetes en desuso | `dotnet list HipoSim.FinancialEngine.sln package --deprecated` | Paquetes marcados como obsoletos. |

No existe un archivo `.editorconfig` ni un `Directory.Build.props` en el repositorio, por lo que todas las reglas son las de por defecto de cada herramienta.

### **2.3. Results**

| Revisión | Resultado |
|:--|:--|
| Compilación por defecto | 0 advertencias y 0 errores. |
| Analizadores Recommended | 179 advertencias, 0 errores (detalle abajo). |
| `dotnet format` | 651 hallazgos en 37 archivos, código de salida 2: 621 de espacio en blanco y 30 sugerencias de estilo de nivel `info`. |
| Paquetes vulnerables | Ninguno, en los siete proyectos, incluidos los transitivos (incluye QuestPDF 2026.9.1). |
| Paquetes en desuso | Ninguno salvo `xunit 2.9.2`, marcado como `Legacy` (sucesor: `xunit.v3`), en los tres proyectos de pruebas. |

**Analizadores Recommended (179):**

| Regla | Cantidad | Dónde | Qué dice |
|:--|--:|:--|:--|
| CA1707 | 166 | Proyectos de pruebas | Guiones bajos en nombres de miembros. |
| CA1305 | 7 | `PythonParityTests` (2), `ContractConformanceTests` (5) | Conversión de texto sin indicar la cultura. |
| CA1822 | 2 | `QuestPdfSimulationReportGenerator.Compose`, `SimulationRequestValidator.Validate` | Miembro que podría ser `static`. |
| CA1859 | 2 | `AuthController`, `SimulationsController` | El método privado podría devolver `ActionResult` en vez de `IActionResult`. |
| CA1869 | 1 | `PythonParityTests` | Se crea un `JsonSerializerOptions` nuevo en vez de reutilizar uno. |
| CA1050 | 1 | `Program.cs` | Tipo declarado fuera de un espacio de nombres. |

**`dotnet format` (651):**

| Tipo | Cantidad | Dónde |
|:--|--:|:--|
| Espacio en blanco | 621 | `HipoSim.Api.Tests/LeadsEndpointTest.cs` (544), `HipoSim.Api/Data/HipoSimDbContext.cs` (46), `ApiHttpTests.cs` (19), `AuthTests.cs` (5) y 7 más en `HipoSim.Api/Leads/`. |
| Sugerencias de estilo `info` | 30 | IDE0305 (15) y otras reglas IDE, CA, SYSLIB y xUnit de una o tres apariciones. |

Ningún hallazgo de espacio en blanco está en el motor financiero, en `HipoSim.Simulations` ni en sus proyectos de pruebas. Todos están en `HipoSim.Api` y `HipoSim.Api.Tests`.

### **2.4. Interpretation and Decisions**

| Hallazgo | Decisión | Motivo |
|:--|:--|:--|
| CA1707 (166) | Aceptado, no se cambia. | Los nombres de prueba usan el formato `Metodo_Cuando_Resultado`, que es una convención del equipo y no un defecto. Si se activan los analizadores en el CI, conviene silenciar esta regla solo para `tests/**`. |
| CA1305 (7) | Pendiente, riesgo bajo. | Son pruebas que convierten números a texto. Pasan hoy; el riesgo es que cambien en un equipo con otra configuración regional. Se corrige con `CultureInfo.InvariantCulture` después de mergear los PR, para no chocar con ellos. |
| CA1822, CA1859, CA1869, CA1050 | Aceptado, no se cambia. | Son sugerencias de rendimiento o de estilo sin efecto funcional. `Program` debe seguir visible para `WebApplicationFactory<Program>`. |
| Espacio en blanco en archivos de `HipoSim.Api` y `HipoSim.Api.Tests` | No se toca en esta rama. | Es código de otras tareas, con cambios en curso. Se recomienda que su autor ejecute `dotnet format whitespace` en su rama; con eso desaparecen los 621 hallazgos. |
| `xunit` en desuso | Aceptado, no se cambia. | Migrar a `xunit.v3` cambia el modelo de las pruebas y no cabe antes del congelamiento. |
| Paquetes vulnerables | Sin acción. | Ninguno encontrado. |

### **2.5. Limitations**

- Es un análisis de un solo momento, sobre la copia descrita en 2.1. No hay una regla activa en el CI: la sección 2.2 es el procedimiento para repetirlo.
- No se ejecutó ninguna herramienta de análisis de seguridad de código (por ejemplo CodeQL o SonarCloud), solo las de la sección 2.2.
- Las reglas son las de por defecto. Un `.editorconfig` del equipo cambiaría los resultados.
- El `dotnet list package --vulnerable` consulta nuget.org en el momento de ejecutarlo; los resultados pueden cambiar con el tiempo.

## **3. Test Counts of the Integration**

Ejecutando `dotnet test HipoSim.FinancialEngine.sln` sobre la copia de la sección 2.1:

| Proyecto | Pruebas | Correctas | Con error | Duración |
|:--|--:|--:|--:|--:|
| `HipoSim.FinancialEngine.Tests` | 109 | 109 | 0 | 496 ms |
| `HipoSim.Simulations.Tests` | 118 | 118 | 0 | 10 s |
| `HipoSim.Api.Tests` | 56 | 56 | 0 | 6 s |
| **Total** | **283** | **283** | **0** | |

La tabla 2.2 de `engine-validation.md` cuenta solo los proyectos del motor y de simulaciones: **227** pruebas (109 + 118). Cada Pull Request, por separado, deja en esa tabla el conteo de su propia rama (188 el de US03 y 205 el de TS03), por eso al mergear los tres hay que actualizar esas cifras a 109, 118 y 227. Esta medición sale de la integración, no de `develop` actual.

## **4. How to Reproduce**

```bash
# Build and tests
dotnet build HipoSim.FinancialEngine.sln
dotnet test HipoSim.FinancialEngine.sln

# Analyzers in Recommended mode
dotnet build HipoSim.FinancialEngine.sln --no-incremental -p:AnalysisMode=Recommended -p:EnableNETAnalyzers=true

# Format check (exit code 2 means there are findings)
dotnet format HipoSim.FinancialEngine.sln --verify-no-changes --no-restore --severity info

# Packages
dotnet list HipoSim.FinancialEngine.sln package --vulnerable --include-transitive
dotnet list HipoSim.FinancialEngine.sln package --deprecated
```
