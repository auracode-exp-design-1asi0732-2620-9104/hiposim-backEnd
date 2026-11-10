# **EXP2. Usage Events Export and KPIs**

Documento de la tarea EXP2 del Sprint 3-4: exportar los eventos de uso a CSV y calcular KPIs. Las herramientas están terminadas y probadas con datos sintéticos; **faltan los datos reales y los nombres reales de columnas y eventos**, que dependen de TS09 (Gustavo) y de TS10 (Landing y móvil).

## **1. Status**

| Pieza | Estado al 2026-11-09 |
|:--|:--|
| Tabla `usage_events` y `POST /api/events` (TS09, Gustavo) | **No está en el repositorio.** Ninguna rama remota contiene archivos con `event`, `usage` o `kpi` en el nombre. |
| Exportador a CSV (`tools/export_usage_events.py`) | Hecho. No supone ninguna columna: exporta las que tenga la tabla. |
| Calculador de KPI (`tools/compute_kpis.py`) | Hecho. Las columnas, los eventos y los KPI se definen en un archivo JSON. |
| Nombres reales de columnas y de eventos | Pendiente de TS09 y TS10. |
| Lista de KPI | Definida por Franco López (autor de la tarea). Un compañero del equipo estuvo de acuerdo, el 2026-11-09, en que la defina él; no hay una aprobación por escrito (sección 3.2). |
| Eventos que los KPI necesitan | Definidos en la sección 3.4. Falta confirmar que Landing y móvil los envían. |
| Resultados | **Ninguno.** Este documento no contiene datos reales ni cifras de uso, porque todavía no se exportó nada. |

## **2. Export to CSV**

### **2.1. Usage**

Requiere Python 3.11 y `psycopg2` (`pip install psycopg2-binary`). La cadena de conexión va en la variable de entorno `DATABASE_URL` y no se acepta como argumento, para que no quede en el historial de la terminal.

```powershell
$env:DATABASE_URL = "postgresql://usuario:clave@host:5432/base"
python tools/export_usage_events.py --out events.csv
python tools/export_usage_events.py --out events.csv --time-column COLUMNA_FECHA --since 2026-11-07 --until 2026-11-11
```

| Opción | Qué hace |
|:--|:--|
| `--out` | Archivo de salida (por defecto `usage_events.csv`). No se sobrescribe un archivo existente salvo con `--force`. |
| `--table` | Tabla a exportar (por defecto `usage_events`; admite `esquema.tabla`). |
| `--time-column`, `--since`, `--until` | Filtran por una columna de fecha. `--since` incluye el límite y `--until` no. Una fecha sin zona se toma como hora de Lima (UTC-5). |
| `--order-by` | Columna para ordenar. |
| `--exclude-columns` | Columnas a omitir, separadas por comas. |

### **2.2. Safety Measures**

- **Solo lectura:** la conexión se abre en modo de solo lectura y la única sentencia es un `SELECT`. Los nombres de tabla y de columna se validan y se escriben como identificadores, no como texto libre.
- **Datos personales:** los eventos no deben tenerlos (requisito de TS09). Si alguna columna tiene un nombre que sugiere datos personales (`email`, `phone`, `dni`, `ip`, `user_id`, `buyer_id` y similares), el script avisa antes de escribir. Es solo un aviso sobre el nombre de la columna: no revisa el contenido. Con `--exclude-columns` se quitan del CSV.
- **Fórmulas en Excel:** el endpoint de eventos es público, así que cualquier texto de la tabla pudo ser escrito por un desconocido. Un texto que empiece por `=`, `+`, `-` o `@` se exporta con una comilla simple delante para que Excel no lo ejecute como fórmula. Los números negativos no se tocan.
- **Formato:** fechas en ISO 8601 con su zona, columnas JSON como JSON compacto y valores nulos como vacío. El archivo es UTF-8.

### **2.3. Smoke Test Against a Real Database (Not Run Yet)**

Las pruebas automáticas usan una conexión simulada, porque el motor de Docker estaba apagado al hacer esta tarea. **La conexión real a PostgreSQL no se probó.** Receta para hacerlo con la base local de desarrollo (ver el README, apartado Getting started), con una tabla de prueba que no toca datos reales:

```powershell
docker compose up -d postgres
docker compose exec postgres psql -U hiposim -d hiposim_dev -c "CREATE TABLE usage_events_smoke (id serial PRIMARY KEY, event_name text, occurred_at timestamptz DEFAULT now(), session_id text);"
docker compose exec postgres psql -U hiposim -d hiposim_dev -c "INSERT INTO usage_events_smoke (event_name, session_id) VALUES ('prueba', 's1'), ('=prueba', 's1');"
$env:DATABASE_URL = "postgresql://hiposim:hiposim_dev_only@localhost:5433/hiposim_dev"
python tools/export_usage_events.py --table usage_events_smoke --out smoke.csv
docker compose exec postgres psql -U hiposim -d hiposim_dev -c "DROP TABLE usage_events_smoke;"
```

Resultado esperado: `smoke.csv` con el encabezado `id,event_name,occurred_at,session_id` y dos filas; en la segunda el nombre del evento aparece como `'=prueba`. Después hay que borrar `smoke.csv`.

## **3. KPIs**

### **3.1. How the Calculator Works**

`tools/compute_kpis.py` lee el CSV y un archivo JSON de configuración (plantilla: `tools/kpi_config.example.json`). La plantilla trae marcadores en mayúsculas (`COLUMNA_NOMBRE_EVENTO`, `EVENTO_SIMULACION_COMPLETADA`...) que hay que reemplazar por los nombres reales.

| Tipo de KPI | Qué calcula |
|:--|:--|
| `count` | Número de eventos con el nombre indicado (sin nombre, todos). |
| `sessions` | Sesiones distintas que tienen el evento indicado (sin evento, todas). |
| `ratio` | Un KPI dividido entre otro definido antes en la lista. Si el divisor es 0 muestra "sin datos". |
| `median_seconds_between` | Por sesión, segundos desde el primer evento `from` (por defecto, el primero de la sesión) hasta el primer evento `to`; la mediana de las sesiones que tienen ambos. Muestra cuántas sesiones se midieron. |

Si la configuración incluye una columna de origen (`source`, por ejemplo Landing o móvil), cada KPI se calcula también por cada origen. El resultado sale como tabla Markdown y, con `--out`, como CSV con el numerador y el denominador de cada razón.

```powershell
python tools/compute_kpis.py --events events.csv --config kpi_config.json --list-events
python tools/compute_kpis.py --events events.csv --config kpi_config.json --out kpis.csv --markdown kpis.md
```

`--list-events` muestra los nombres de evento que hay en el CSV con su cantidad, para completar la configuración sin adivinar. Si la configuración nombra un evento que no existe, el script avisa y muestra los que sí hay (así se detecta un error de escritura).

### **3.2. KPI List**

Lista definida por el autor de la tarea (Franco López). Un compañero del equipo estuvo de acuerdo, el 2026-11-09, en que la defina él; no existe una definición escrita del curso ni del equipo. Si se agrega, quita o cambia un KPI, se edita el JSON de configuración y se actualiza esta tabla. Los nombres de evento siguen por confirmar con TS09 y TS10.

| Id | KPI | Qué mide | Cómo se calcula | Unidad |
|:--|:--|:--|:--|:--|
| `sessions` | Sesiones | Cuántas sesiones distintas generaron eventos. | `sessions` sin evento. | sesiones |
| `sim_started` | Simulaciones iniciadas | Cuántas veces alguien empezó una simulación. | `count` del evento de simulación iniciada. | eventos |
| `sim_completed` | Simulaciones completadas | Cuántas veces se llegó a un resultado. | `count` del evento de simulación completada. | eventos |
| `completion_rate` | Tasa de finalización | Qué parte de las simulaciones iniciadas termina. | `ratio`: `sim_completed` entre `sim_started`. | % (con "n de m") |
| `sessions_with_sim` | Sesiones con simulación completada | Cuántas sesiones obtuvieron un resultado. | `sessions` con el evento de simulación completada. | sesiones |
| `sessions_with_contact` | Sesiones con contacto enviado | Cuántas sesiones contactaron a una inmobiliaria. | `sessions` con el evento de contacto enviado. | sesiones |
| `contact_conversion` | Conversión a contacto | Qué parte de las sesiones con resultado llega a contactar a una inmobiliaria. | `ratio`: `sessions_with_contact` entre `sessions_with_sim`. | % (con "n de m") |
| `pdf_downloads` | Descargas de PDF | Uso del reporte PDF (TS03 y US08). | `count` del evento de descarga de PDF. | eventos |
| `time_to_first_sim` | Tiempo hasta la primera simulación | Qué tan rápido llega una sesión a un resultado. | `median_seconds_between`: del primer evento de la sesión al primer evento de simulación completada. | segundos (con "n sesiones medidas") |

Los mismos nueve KPI están en `tools/kpi_config.example.json`, con marcadores en lugar de nombres reales. Un KPI cuyo evento no exista en los datos sale como 0 o "sin datos", nunca con un valor inventado.

**Lo que esta herramienta no calcula:** el uso del bono y de la gracia, si TS09 los guarda dentro de una columna JSON y no como eventos distintos, porque el calculador solo lee nombres de evento. Esos datos sí existen en las simulaciones guardadas por `POST /api/simulations`; si el equipo los quiere como KPI hay que consultarlos aparte.

### **3.3. How to Read the Results**

- La muestra son las personas de EXP1 (al menos 2 por integrante): serán pocas decenas de sesiones como máximo. Hay que informar los conteos junto al porcentaje ("2 de 3"), que es lo que ya muestra la tabla, y no generalizar a otros usuarios.
- Una persona que usa Landing y la app genera sesiones distintas; el calculador no las une.
- Los datos de quién probó, cuándo y qué hizo (EXP1) se anotan a mano y no salen de esta herramienta. Nunca se inventan.

### **3.4. Events and Fields the KPIs Need**

Para que la lista de la sección 3.2 se pueda calcular, los datos deben tener estos eventos y campos. Lo que falte aparece como 0 o "sin datos" y se informa así, sin reemplazarlo por otra cosa.

| Evento necesario | KPI que lo usan |
|:--|:--|
| Simulación iniciada | `sim_started`, `completion_rate` |
| Simulación completada (se llegó al resultado) | `sim_completed`, `completion_rate`, `sessions_with_sim`, `contact_conversion`, `time_to_first_sim` |
| Contacto enviado a una inmobiliaria | `sessions_with_contact`, `contact_conversion` |
| PDF descargado | `pdf_downloads` |

| Campo necesario en cada evento | KPI que lo usan |
|:--|:--|
| Nombre del evento | Todos |
| Identificador de sesión o de visitante anónimo | `sessions`, `sessions_with_sim`, `sessions_with_contact`, `contact_conversion`, `time_to_first_sim` |
| Fecha y hora | `time_to_first_sim` y el filtro por la ventana de EXP1 |
| Origen (Landing o móvil) | Opcional: permite ver cada KPI por origen |

## **4. Questions for Gustavo (TS09)**

1. ¿Cómo se llama la tabla y cuáles son sus columnas y tipos? Necesito en concreto: nombre del evento, fecha y hora (¿con zona horaria?), identificador de sesión o de visitante anónimo, origen (Landing o móvil) y datos adicionales.
2. ¿Qué nombres de evento enviarán el Landing (TS10a) y el móvil (TS10b)? ¿Existen los cuatro eventos de la sección 3.4 (simulación iniciada, simulación completada, contacto enviado y PDF descargado)? Si alguno no existe, ¿se puede agregar antes del congelamiento?
3. ¿Cómo se identifica una sesión y cambia el identificador cada vez que se abre la aplicación?
4. ¿Hay alguna columna con datos personales? No debería haber ninguna.
5. ¿El `POST /api/events` ya está en Render y desde cuándo hay datos?
6. ¿Las opciones del bono y de la gracia se guardan como evento propio o dentro de un JSON?

## **5. What Remains**

1. Que TS09 llegue a `develop` y que Landing y móvil envíen los eventos de la sección 3.4.
2. Copiar `tools/kpi_config.example.json` como `kpi_config.json` y reemplazar los marcadores con los nombres reales (`--list-events` ayuda).
3. Correr la receta de la sección 2.3 una vez, para comprobar la conexión real.
4. Exportar los eventos de la ventana de EXP1 (del 7 al 10 de noviembre) y calcular los KPI.
5. Pegar la tabla y el CSV en el informe, junto con el registro manual de los probadores.

## **6. Verification**

| Qué | Cómo |
|:--|:--|
| Pruebas automáticas | 32 pruebas de `unittest` (`python -m unittest discover -s tools/tests -v`), todas con datos **sintéticos** escritos en el propio archivo de pruebas. Cubren el formato de cada tipo de valor, la protección contra fórmulas, el CSV con comas, comillas y saltos de línea, las opciones, los cuatro tipos de KPI, los grupos, los errores de configuración y de CSV, y la salida de la línea de comandos. |
| Mutaciones | 10 cambios deliberados y temporales en el código. En la primera ronda se detectaron 9; el décimo (el orden de los grupos) sobrevivió porque la prueba usaba solo 2 grupos, así que se agregó una prueba con 5 y ahora se detecta en 5 de 5 intentos. Después de cada cambio se restauró el original. |
| Salida real | Se ejecutó la línea de comandos sobre un CSV sintético de 7 eventos y se revisó la tabla Markdown y el CSV; eso llevó a corregir los conteos que salían como `3.0` y el texto "1 sesiones". |
| **No verificado** | La conexión real a PostgreSQL (`connect_read_only` y la consulta con `psycopg2.sql`); ver la receta de la sección 2.3. Tampoco se probó con la base de Render. |
