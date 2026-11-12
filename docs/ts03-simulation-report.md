# **TS03. Simulation PDF Report**

Documento de la tarea TS03 del Sprint 3-4: `GET /api/simulations/{id}/report`, que devuelve el resumen de una simulación y su cronograma completo como PDF.

## **1. Endpoint**

| Elemento | Detalle |
|:--|:--|
| Ruta | `GET /api/simulations/{id}/report`, donde `id` es el `simulationId` que devolvió `POST /api/simulations`. |
| Autenticación | Opcional. Con `Authorization: Bearer <token>` de un comprador, la solicitud se asocia a ese comprador. |
| 200 | `application/pdf`, enviado como adjunto con el nombre `hiposim-simulacion-{id}.pdf`. |
| 403 | `application/problem+json`, título `The simulation belongs to another user.` |
| 404 | `application/problem+json`, título `Simulation not found.` También se responde 404 cuando el `id` no es un GUID. |

El contrato está en `docs/api-contract/openapi.v0.yaml` (operación `getSimulationReport`) y las pruebas de conformidad lo comparan con el endpoint real.

## **2. Access Rules**

| Simulación | Quién consulta | Resultado |
|:--|:--|:--|
| Calculada sin iniciar sesión (sin comprador) | Cualquiera que tenga el `id` | 200 |
| Calculada por un comprador | Ese comprador | 200 |
| Calculada por un comprador | Otro comprador | 403 |
| Calculada por un comprador | Sin sesión | 403 |
| No existe | Cualquiera | 404 |

El `id` es un GUID aleatorio que solo recibe quien creó la simulación, por eso una simulación anónima se puede descargar con solo conocerlo. Estas reglas son una suposición del autor de la tarea: el Jira pide 200, 404 y 403 pero no define quién recibe el 403. Quedan por confirmar con el equipo (ver sección 7).

## **3. Content of the Report**

El PDF es A4 vertical, en español, y tiene estas partes:

1. Título, código de la simulación y fecha de cálculo en hora de Lima.
2. **Datos de la simulación:** precio, cuota inicial, bono del Buen Pagador, monto financiado, TEA, plazo, periodo de gracia y fecha de inicio.
3. **Resultados:** cuota mensual, total pagado, total de intereses, costos adicionales, TCEA, TIR anual y VAN.
4. **Supuestos del cálculo:** seguros, gastos administrativos y tasa de descuento del VAN.
5. Notas (método francés, TCEA con seguros, filas de gracia, aviso de documento referencial).
6. **Cronograma de pagos** con una fila por cuota (hasta 360 según el plazo máximo del contrato): período, fecha, saldo inicial, interés, amortización, cuota, seguros, pago total y saldo final. Las filas de gracia van sombreadas y el encabezado de la tabla se repite en cada página.

Formato: montos como `S/ 1,234.56` (negativos como `-S/ 17,712.02`), porcentajes con 2 decimales, fechas `dd/MM/yyyy`. Se usa la cultura invariante a propósito para que el resultado no dependa de los datos ICU del servidor. Todos los valores salen de la simulación guardada, el reporte no recalcula nada.

## **4. Design**

| Pieza | Responsabilidad |
|:--|:--|
| `ISimulationRepository.FindAsync` | Busca la simulación guardada. `EfSimulationRepository` la reconstruye desde las columnas JSON. |
| `SimulationReportService` | Aplica las reglas de acceso y pide el PDF. No conoce la biblioteca de PDF. |
| `SimulationReportContentBuilder` | Convierte el registro en texto ya formateado (`SimulationReportContent`). Se prueba sin generar ningún PDF. |
| `ISimulationReportGenerator` | Punto de cambio de la biblioteca: recibe el contenido y devuelve los bytes. |
| `QuestPdfSimulationReportGenerator` | Implementación con QuestPDF. |

## **5. PDF Library (Provisional)**

Se usó **QuestPDF 2026.9.1** con la licencia Community (`QuestPDF.Settings.License = LicenseType.Community`).

- **Por qué:** API fluida en .NET puro, tablas con encabezado repetido, paginación y fuente (Lato) incluidas en el paquete, y binarios nativos para Windows y Linux dentro del paquete.
- **Licencia:** según el `LICENSE.md` del paquete, la licencia Community es gratuita para uso individual, de aprendizaje, instituciones académicas, proyectos de código abierto y organizaciones con ingresos anuales menores a USD 1,000,000; las entidades del sector público y las empresas que cotizan en bolsa no califican. El proyecto es un trabajo universitario, pero si se comercializa hay que revisar la licencia otra vez.
- **Estado:** la investigación SP01 de Gustavo sobre la biblioteca de PDF no estaba publicada cuando se hizo esta tarea. Cambiar de biblioteca solo exige otra implementación de `ISimulationReportGenerator`; el controlador, las reglas de acceso, el contenido y las pruebas de contenido no cambian.

## **6. Verification**

| Qué | Cómo |
|:--|:--|
| Contenido del reporte | `SimulationReportContentBuilderTests` (16): formato de montos y fechas, bono aplicado, solo elegible o no elegible, gracia total y parcial, filas de gracia, seguros, hora de Lima, signo negativo del VAN, sin rayas largas. |
| Reglas de acceso | `SimulationReportServiceTests` (6), con un generador falso que cuenta las llamadas: no se genera nada si la respuesta es 403 o 404. |
| HTTP con el controlador real | `SimulationReportEndpointTests` (9): 200 con cabeceras y marcas `%PDF-` y `%%EOF`, 403 de otro comprador y de quien no inició sesión, 404 de un GUID desconocido y de ids que no son GUID. |
| Contrato | `ContractConformanceTests` (+3): tipo de contenido, nombre de archivo y los problemas 403 y 404 documentados. |
| Extremo a extremo con base de datos | `HipoSim.Api.Tests` (+2): registro de dos compradores con JWT, simulación guardada en EF Core (InMemory) y leída de vuelta (fechas, enumeraciones y 240 filas sobreviven al JSON), y el documento Swagger describe la operación. |
| Generador PDF | `QuestPdfSimulationReportGeneratorTests` (5): PDF bien formado, 2 páginas con 60 cuotas y 5 con 240, mismas páginas al renderizar dos veces. |
| Revisión visual | Las páginas se renderizaron como imagen y se revisaron a ojo. La primera revisión mostró las notas solas en una última página; se movieron antes del cronograma y la prueba de 240 filas ahora exige 5 páginas. |
| Mutaciones | Cinco cambios deliberados y temporales (ver `engine-validation.md`, sección 2.5); los cinco fueron detectados. |

## **7. Limitations and Pending**

- **No se probó en Linux ni en la imagen Docker de Render:** el motor de Docker no estaba disponible. Una revisión estática de las bibliotecas nativas de Linux del paquete (`libQuestPdfSkia.so`, `libqpdf.so`) solo encontró dependencias de `libc`, `libm`, `libgcc_s`, `libstdc++`, `libz` y `libpthread`, sin referencias a `fontconfig`, por lo que no se modificó el `Dockerfile`. Falta comprobarlo con un `docker build` y una descarga real.
- **Asesores:** un asesor no es el comprador, así que recibe 403 en una simulación con comprador. Si los asesores deben ver el reporte de la simulación de un lead (`Lead.SimulationId`), hay que definir esa regla.
- **Texto provisional:** las notas del PDF, incluido el aviso "No constituye una oferta de crédito", son del autor de la tarea y no están validadas por el equipo.
- **Sin caché:** el PDF se genera en cada solicitud. No se midió el tiempo de generación bajo carga.
- **Cobertura:** no se volvió a medir después de TS03.
