# HipoSim Financial Engine

Motor financiero y endpoint de simulación (`POST /api/simulations`) del backend de **HipoSim**, el simulador de
créditos hipotecarios del curso de Desarrollo de Aplicaciones Web y Móviles (UPC). El motor es un port a C# del motor
de **AutoFinance Pro** (Python), validado por paridad numérica contra el original.

> Estado: contrato de API v0.1.0 en revisión del equipo. Este repositorio es la parte de Franco López (SP02, TS06 y
> contrato v0); la integración con el host de la API (`hiposim-platform`) queda pendiente.

## Structure

| Carpeta | Contenido |
|---|---|
| `src/HipoSim.FinancialEngine` | Motor financiero puro (net8.0): método francés, gracia total o parcial, cuota balón, TEA a tasa mensual, VAN, TIR, TCEA y bono del buen pagador. Sin dependencias externas. |
| `src/HipoSim.Simulations` | Endpoint `POST /api/simulations` (net8.0): validación, servicio, controlador y contratos JSON. Se registra con `AddSimulations()`. |
| `tests/HipoSim.FinancialEngine.Tests` | 106 pruebas del motor, incluida la paridad contra Python (48 escenarios en `GoldenData/python-parity-cases.json`). |
| `tests/HipoSim.Simulations.Tests` | 60 pruebas del endpoint: validador, servicio, HTTP real con TestServer y conformidad con el contrato OpenAPI. |
| `samples/HipoSim.EnginePrototype` | Consola que ejecuta el ejemplo del contrato (S/ 280,000, TEA 8.5 %, 20 años, bono solicitado). |
| `docs/api-contract/openapi.v0.yaml` | Contrato OpenAPI 3.0.3 v0.1.0 (health, registro, login y simulación). |
| `docs/engine-validation.md` | Documento de validación del motor y Testing Suite Evidence. |
| `docs/Informe-avance-y-contrato-API-v0.docx` | Informe de avance y contrato v0 para revisión de Carlos. |
| `tools/generate_parity_cases.py` | Regenera los datos de paridad ejecutando el motor original de Python. |

## Requirements

- SDK de .NET 9 (las librerías apuntan a net8.0, las pruebas y el prototipo a net9.0).
- Para regenerar los datos de paridad: una copia de AutoFinance Pro con su entorno virtual (Python 3.11).

## Getting started

```bash
dotnet build
dotnet test
dotnet run --project samples/HipoSim.EnginePrototype
```

Cobertura (coverlet):

```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Using the endpoint from an API host

`HipoSim.Simulations` no es un host: el host de la API debe registrar el endpoint y la persistencia.

```csharp
builder.Services.AddSimulations(builder.Configuration);
builder.Services.AddScoped<ISimulationRepository, EfSimulationRepository>(); // implementación del host (EF Core)
```

- `ISimulationRepository` es el punto de persistencia: recibe un `SimulationRecord` por cada cálculo exitoso.
- Los supuestos (seguros, gastos administrativos y tasa de descuento) se leen de la sección `Simulation` de la
  configuración; si no existe, se usan los valores por defecto de `SimulationDefaults`.
- La autenticación es opcional: si el token trae `sub` o `NameIdentifier`, se guarda como comprador.
- Los errores salen como `application/problem+json` con las claves en camelCase de la petición.

## Regenerating the Python parity data

Solo hace falta si cambia el motor original. Con el entorno virtual de AutoFinance Pro:

```bash
<autofinance-pro-backend>/.venv/Scripts/python.exe tools/generate_parity_cases.py <autofinance-pro-backend>
```

La ruta también puede pasarse con la variable de entorno `AUTOFINANCE_PRO_PATH`. Si los datos cambian, hay que volver a
correr `dotnet test` y revisar las diferencias antes de aceptarlas.

## Contract

El contrato vive en `docs/api-contract/openapi.v0.yaml`. Las pruebas de conformidad envían sus ejemplos al endpoint real,
así que el YAML y el código no pueden separarse sin que falle una prueba. Los cambios al contrato se acuerdan con el
equipo de backend (Carlos) y con los clientes web y móvil antes de implementarse. Las preguntas abiertas están en la
sección correspondiente del informe.

## Scope and limitations

- Del contrato solo se implementa `POST /api/simulations` (TS06). `/health`, `/api/auth/register` y `/api/auth/login`
  están documentados para el host de la API y no se implementan aquí.
- El bono del buen pagador usa los tramos referenciales de la Landing Page; no se han contrastado con la normativa vigente.
- Solo se calcula con tasa efectiva anual (TEA) y plazo en meses; el motor admite más variantes que el endpoint aún no expone.
- No hay integración continua configurada todavía.

## Conventions

- GitFlow (Capítulo 5 del informe): `main` y `develop` son permanentes; las ramas `feature/<alcance>-<descripcion-corta>`
  nacen de `develop` y vuelven por Pull Request revisado por al menos un compañero. No se hacen cambios directos en `main`.
- Commits en formato Conventional Commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`).
