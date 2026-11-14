# **SP01. PDF Library for the Simulation Report**

Documento de la tarea SP01 (Sprint 3): investigar bibliotecas de generación de PDF para ASP.NET Core y recomendar una para el reporte de simulación, con un prototipo mínimo que genera el cronograma en PDF.

## **1. Resumen**

| Pregunta | Respuesta |
|:--|:--|
| Qué se recomienda | **QuestPDF**, con la licencia Community. |
| Dónde está el prototipo | `QuestPdfSimulationReportGenerator` de la tarea TS03 (rama `feature/ts03-simulation-report`). Genera el resumen y el cronograma completo de una simulación. |
| Qué riesgo queda | La licencia si el producto se comercializa, y que no se probó en Linux (imagen de Render). |
| Qué cuesta cambiar de biblioteca | Una implementación nueva de `ISimulationReportGenerator`. El endpoint, las reglas de acceso y el texto del reporte no cambian. |

## **2. Criterios de evaluación**

La tarea pide tres criterios. Se agregaron dos más, por el despliegue real del proyecto.

| Criterio | Qué se mira |
|:--|:--|
| **Licencia** | Si se puede usar gratis en un trabajo universitario y qué pasa si el producto se comercializa. |
| **Calidad de salida** | Tablas largas (hasta 360 cuotas), encabezado repetido en cada página, paginación y tipografía legible. |
| **Facilidad de integración** | Cuánto código y cuántas dependencias exige en ASP.NET Core, y si se puede probar sin abrir el PDF. |
| Funciona en Linux | La API se despliega con Docker en Render, que es Linux. |
| Mantenimiento | Si la biblioteca sigue activa. |

## **3. Opciones evaluadas**

| Opción | Cómo genera el PDF | Licencia | Ajuste al proyecto |
|:--|:--|:--|:--|
| **QuestPDF** | Código C# con API fluida (documento, páginas, tablas). Paginación y encabezado de tabla repetido incluidos. | Community: gratuita para uso individual, aprendizaje, instituciones académicas, proyectos de código abierto y organizaciones con ingresos anuales menores a USD 1,000,000. Comercial en otro caso. | **Alto.** Ideal para un reporte con tabla larga. Es .NET puro, sin navegador. |
| **PDFsharp y MigraDoc** | Dibujo a bajo nivel (PDFsharp) o documento por párrafos y tablas (MigraDoc). | MIT. | Medio. Licencia más simple, pero hay que resolver más a mano (fuentes, saltos de página, encabezados repetidos). |
| **iText Core** | API completa de PDF. | AGPL 3.0, o licencia comercial. La AGPL obliga a publicar el código de quien lo use en un servicio de red. | Bajo. La AGPL es un problema para un servicio que podría comercializarse. |
| **HTML a PDF con un navegador sin interfaz** (por ejemplo, PuppeteerSharp) | Se arma una página HTML y un Chromium la imprime. | MIT para la biblioteca; Chromium se descarga aparte. | Bajo. Buena fidelidad visual, pero exige instalar un navegador en la imagen de Docker: pesado y frágil en el plan gratuito de Render. |

Las licencias salen de la documentación oficial de cada proyecto y pueden cambiar. **Antes de comercializar HipoSim hay que releerlas.**

## **4. Recomendación**

**QuestPDF con la licencia Community.**

1. **Calidad de salida.** El reporte necesita una tabla de hasta 360 filas con el encabezado repetido en cada página. QuestPDF lo resuelve de fábrica. En el prototipo, 60 cuotas ocupan 2 páginas y 240 cuotas ocupan 5.
2. **Integración.** Es una sola dependencia de .NET, sin navegador ni servicios externos. El contenido del reporte se prepara aparte (`SimulationReportContentBuilder`) y se prueba sin generar ningún PDF.
3. **Licencia.** El proyecto es un trabajo universitario, que entra en la licencia Community.
4. **Despliegue.** Trae sus binarios para Windows y Linux dentro del paquete, sin depender de un navegador.

## **5. Prototipo**

El prototipo es la implementación de TS03.

| Pieza | Responsabilidad |
|:--|:--|
| `ISimulationReportGenerator` | Punto de cambio de la biblioteca: recibe el contenido y devuelve los bytes del PDF. |
| `QuestPdfSimulationReportGenerator` | Implementación con QuestPDF: A4 vertical, bloque de resumen y cronograma completo. |
| `SimulationReportContentBuilder` | Convierte la simulación guardada en texto ya formateado. |
| `GET /api/simulations/{id}/report` | Devuelve el PDF. Detalle en `docs/ts03-simulation-report.md`. |

Verificación hecha en TS03: el PDF sale bien formado, con 2 páginas para 60 cuotas y 5 para 240, y las páginas se revisaron a ojo.

## **6. Riesgos y qué hacer**

| Riesgo | Mitigación |
|:--|:--|
| El producto se comercializa y la licencia Community ya no aplica | Releer la licencia. Cambiar de biblioteca solo exige otra implementación de `ISimulationReportGenerator`. |
| No se probó en Linux ni en la imagen de Render | Hacer un `docker build` y descargar un reporte desplegado. Una revisión estática de las bibliotecas nativas del paquete no encontró dependencias de `fontconfig`, solo de la biblioteca estándar de C. |
| El tiempo de generación bajo carga no se midió | Medirlo si se publica el reporte a más usuarios. Hoy se genera en cada solicitud, sin caché. |

## **7. Qué no se verificó**

- La licencia de cada opción se tomó de su documentación oficial, sin consultar a un abogado.
- No se generó el mismo reporte con PDFsharp, iText ni PuppeteerSharp: la comparación de calidad de salida de esas opciones es por sus características, no por una prueba lado a lado.
- La generación en Linux y en Render queda pendiente.
