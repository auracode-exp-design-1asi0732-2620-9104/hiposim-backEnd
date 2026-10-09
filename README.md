# HipoSim Platform API + Financial Engine

Backend ASP.NET Core de **HipoSim**, simulador de créditos hipotecarios. Integra la API ejecutable,
registro/login JWT, PostgreSQL (EF Core con migraciones), persistencia de simulaciones y el motor financiero.
El motor de Franco López es un port a C# de **AutoFinance Pro** (Python), validado previamente por paridad numérica.

> Estado: host HipoSim.Api agregado sobre la versión `develop` suministrada (no se borraron componentes anteriores).
> El contrato OpenAPI v0.1.0 sigue siendo borrador sujeto a acuerdos con el equipo.
> Antes del merge, ejecutar `dotnet build` y `dotnet test` con SDK 9 y confirmar la API con Swagger y PostgreSQL.

## Structure

| Carpeta | Contenido |
|---|---|
| `src/HipoSim.Api` | Host API ASP.NET Core net9.0, JWT, EF Core, migraciones PostgreSQL, seed de desarrollo y Swagger. |
| `tests/HipoSim.Api.Tests` | Nuevas pruebas de validación, registro, contraseñas, duplicados, login y claims JWT. |
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

- .NET SDK 9; bibliotecas originales net8.0 y host/pruebas net9.0.
- PostgreSQL 16 (directamente o mediante Docker).
- Para regenerar paridad: AutoFinance Pro + Python 3.11, solo si se modifica el motor.
- Instrucciones locales en el apartado **Getting started** de este README.

## Getting started (Windows PowerShell / Git Bash)

1. Instalar **.NET SDK 9** y **Docker Desktop** con el motor de Linux en ejecución.
2. Abrir una terminal en la carpeta que contiene `HipoSim.FinancialEngine.sln`.
3. Ejecutar los siguientes comandos en orden:

```powershell
docker compose up -d postgres
docker compose ps
docker compose exec postgres pg_isready -U hiposim -d hiposim_dev
dotnet restore HipoSim.FinancialEngine.sln
dotnet build HipoSim.FinancialEngine.sln
dotnet test HipoSim.FinancialEngine.sln
dotnet run --project src/HipoSim.Api --launch-profile http
```

Swagger: `http://localhost:5000/swagger` — comprobar primero `GET /health`, luego `POST /api/auth/register` y `POST /api/auth/login`.

**Configuración PostgreSQL local:** usuario `hiposim`, contraseña `hiposim_dev_only`, base de datos `hiposim_dev` y **puerto de Windows 5433** (puerto del contenedor 5432). Los datos permanecen en el volumen `hiposim_pgdata`. Para cambiar el puerto de Windows, modificar `HIPOSIM_POSTGRES_PORT` en el entorno **y** actualizar `Port` en `src/HipoSim.Api/appsettings.Development.json` o suministrar `ConnectionStrings__HipoSimDb`.

Para detener: `Ctrl+C` en la terminal de la API y `docker compose down` para PostgreSQL. No ejecutar `docker compose down -v` si quieres conservar los datos.

Si `dotnet test` falla, **no hacer merge**: revisar las salidas de los proyectos de pruebas, especialmente `HipoSim.Api.Tests`. GitHub Actions (`.github/workflows/api-ci.yml`) también ejecuta restore/build/test en PR contra `develop` o `main`.

La configuración de desarrollo contiene credenciales **solo locales**. En despliegues reales debes configurar
`ConnectionStrings__HipoSimDb` y `Jwt__SigningKey` mediante variables secretas (nunca subir claves de producción).

Cobertura (coverlet):

```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Using the endpoint from an API host

`src/HipoSim.Api` es el host ejecutable. Registra `AddSimulations(...)` y `EfSimulationRepository`
con `HipoSimDbContext` sin alterar el motor de cálculo existente.

- `ISimulationRepository` es el punto de persistencia: recibe un `SimulationRecord` por cada cálculo exitoso.
- Los supuestos (seguros, gastos administrativos y tasa de descuento) se leen de la sección `Simulation` de la
  configuración; si no existe, se usan los valores por defecto de `SimulationDefaults`.
- La autenticación en simulaciones es opcional: se persiste con identificador de comprador solo si el JWT tiene el rol `buyer`.
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

- Se implementan `GET /health`, `POST /api/auth/register`, `POST /api/auth/login` y el existente `POST /api/simulations`.
- La pantalla visual de registro de US13 pertenece al repositorio **móvil**; este backend implementa su API.
- No se han desplegado recursos reales ni configurado protecciones de rama en GitHub desde este paquete.
- Las evidencias de ejecución/despliegue requieren capturas reales al ejecutarlo; no se incluyen evidencias simuladas.
- El bono del buen pagador usa los tramos referenciales de la Landing Page; no se han contrastado con la normativa vigente.
- Solo se calcula con tasa efectiva anual (TEA) y plazo en meses; el motor admite más variantes que el endpoint aún no expone.
- Hay un workflow `.github/workflows/api-ci.yml` para restore, build y tests en PR/push a `develop` y `main`.

## Conventions

- GitFlow (Capítulo 5 del informe): `main` y `develop` son permanentes; las ramas `feature/<alcance>-<descripcion-corta>`
  nacen de `develop` y vuelven por Pull Request revisado por al menos un compañero. No se hacen cambios directos en `main`.
- Commits en formato Conventional Commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`).

