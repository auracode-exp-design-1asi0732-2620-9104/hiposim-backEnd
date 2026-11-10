#!/usr/bin/env python3
"""Computes the EXP2 KPIs from the CSV written by export_usage_events.py.

The script knows nothing about the event table: the column names and the event names are read from a JSON
configuration file (see tools/kpi_config.example.json), so it can be pointed at the real TS09 schema without
changing code. Only the standard library is used.

    python tools/compute_kpis.py --events events.csv --config kpi_config.json --list-events
    python tools/compute_kpis.py --events events.csv --config kpi_config.json --out kpis.csv --markdown kpis.md

KPI types (each one is an object in the "kpis" list of the configuration):
    count                   events with the given name ("event"); without it, all the events.
    sessions                distinct sessions that have the given event ("event"); without it, all the sessions.
    ratio                   "numerator" / "denominator", the ids of two KPIs defined earlier in the list.
    median_seconds_between  per session, seconds from the first "from" event (default: the first event of the
                            session) to the first "to" event; the median over the sessions that have both.

With a "source" column configured, every KPI is also computed for each distinct value of that column.
"""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import json
import statistics
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass
from typing import Any, Sequence

KPI_TYPES = ("count", "sessions", "ratio", "median_seconds_between")
TOTAL = "Total"


class ConfigError(ValueError):
    """The configuration or the CSV does not match what the KPIs need."""


@dataclass(frozen=True)
class Event:
    name: str
    time: dt.datetime | None
    session: str
    group: str


@dataclass(frozen=True)
class Result:
    value: float | None
    numerator: float | None = None
    denominator: float | None = None
    sessions_measured: int | None = None


def validate_config(config: Any) -> dict:
    """Checks the configuration and returns it; raises ConfigError with a message a person can act on."""
    if not isinstance(config, dict):
        raise ConfigError("La configuración debe ser un objeto JSON.")
    columns = config.get("columns")
    if not isinstance(columns, dict) or not columns.get("event"):
        raise ConfigError('La configuración necesita "columns" con al menos "event" (columna del nombre del evento).')
    kpis = config.get("kpis")
    if not isinstance(kpis, list) or not kpis:
        raise ConfigError('La configuración necesita una lista "kpis" con al menos un KPI.')

    seen: list[str] = []
    for position, kpi in enumerate(kpis, start=1):
        if not isinstance(kpi, dict) or not kpi.get("id"):
            raise ConfigError(f"El KPI número {position} no tiene \"id\".")
        kpi_id, kind = kpi["id"], kpi.get("type")
        if kpi_id in seen:
            raise ConfigError(f'El id "{kpi_id}" está repetido.')
        if kind not in KPI_TYPES:
            raise ConfigError(f'El KPI "{kpi_id}" tiene un tipo inválido ({kind!r}). Tipos: {", ".join(KPI_TYPES)}.')
        if kind in ("sessions", "median_seconds_between") and not columns.get("session"):
            raise ConfigError(f'El KPI "{kpi_id}" necesita "columns.session".')
        if kind == "median_seconds_between":
            if not columns.get("time"):
                raise ConfigError(f'El KPI "{kpi_id}" necesita "columns.time".')
            if not kpi.get("to"):
                raise ConfigError(f'El KPI "{kpi_id}" necesita "to" (evento de llegada).')
        if kind == "ratio":
            for side in ("numerator", "denominator"):
                if kpi.get(side) not in seen:
                    raise ConfigError(f'El KPI "{kpi_id}" usa {side}={kpi.get(side)!r}, que no está definido antes en la lista.')
        seen.append(kpi_id)
    return config


def parse_time(text: str, line: int) -> dt.datetime | None:
    """Parses an ISO 8601 date time; a value without offset is taken as UTC so all of them can be compared."""
    text = text.strip()
    if not text:
        return None
    try:
        value = dt.datetime.fromisoformat(text)
    except ValueError as error:
        raise ConfigError(f"Línea {line}: '{text}' no es una fecha ISO 8601.") from error
    return value if value.tzinfo else value.replace(tzinfo=dt.timezone.utc)


def load_events(path: str, columns: dict) -> list[Event]:
    with open(path, newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        header = reader.fieldnames or []
        wanted = [name for key in ("event", "time", "session", "source") if (name := columns.get(key))]
        missing = [name for name in wanted if name not in header]
        if missing:
            raise ConfigError(f"El CSV no tiene las columnas {', '.join(missing)}. Columnas del CSV: {', '.join(header)}.")
        events = []
        for row in reader:
            events.append(Event(
                name=(row[columns["event"]] or "").strip(),
                time=parse_time(row[columns["time"]] or "", reader.line_num) if columns.get("time") else None,
                session=(row[columns["session"]] or "").strip() if columns.get("session") else "",
                group=(row[columns["source"]] or "").strip() if columns.get("source") else "",
            ))
    return events


def _matching(events: Sequence[Event], name: str | None) -> list[Event]:
    return [event for event in events if name is None or event.name == name]


def _median_seconds(events: Sequence[Event], start_name: str | None, end_name: str) -> tuple[float | None, int]:
    by_session: dict[str, list[Event]] = defaultdict(list)
    for event in events:
        if event.session and event.time is not None:
            by_session[event.session].append(event)
    durations = []
    for session_events in by_session.values():
        session_events.sort(key=lambda event: event.time)
        starts = [event for event in session_events if start_name is None or event.name == start_name]
        if not starts:
            continue
        start = starts[0]
        ends = [event for event in session_events if event.name == end_name and event.time >= start.time]
        if ends:
            durations.append((ends[0].time - start.time).total_seconds())
    return (statistics.median(durations) if durations else None), len(durations)


def compute(kpis: Sequence[dict], events: Sequence[Event]) -> dict[str, Result]:
    results: dict[str, Result] = {}
    for kpi in kpis:
        kind = kpi["type"]
        if kind == "count":
            results[kpi["id"]] = Result(float(len(_matching(events, kpi.get("event")))))
        elif kind == "sessions":
            sessions = {event.session for event in _matching(events, kpi.get("event")) if event.session}
            results[kpi["id"]] = Result(float(len(sessions)))
        elif kind == "ratio":
            numerator, denominator = results[kpi["numerator"]].value, results[kpi["denominator"]].value
            value = numerator / denominator if denominator else None
            results[kpi["id"]] = Result(value, numerator, denominator)
        else:
            value, measured = _median_seconds(events, kpi.get("from"), kpi["to"])
            results[kpi["id"]] = Result(value, sessions_measured=measured)
    return results


def compute_by_group(config: dict, events: Sequence[Event]) -> list[tuple[str, dict[str, Result]]]:
    """Returns (group, results) for the total and, when a source column is set, for each source."""
    groups = [TOTAL]
    if config["columns"].get("source"):
        groups += sorted({event.group for event in events if event.group})
    return [
        (group, compute(config["kpis"], events if group == TOTAL else [e for e in events if e.group == group]))
        for group in groups
    ]


def configured_event_names(config: dict) -> set[str]:
    names = set()
    for kpi in config["kpis"]:
        for key in ("event", "from", "to"):
            if kpi.get(key):
                names.add(kpi[key])
    return names


def format_value(kpi: dict, result: Result) -> str:
    if result.value is None:
        return "sin datos"
    if kpi["type"] == "ratio":
        return f"{result.value * 100:.1f} %"
    if kpi["type"] == "median_seconds_between":
        return f"{result.value:.1f} s"
    return str(int(result.value))


def detail(kpi: dict, result: Result) -> str:
    if kpi["type"] == "ratio" and result.numerator is not None:
        return f"{int(result.numerator)} de {int(result.denominator or 0)}"
    if kpi["type"] == "median_seconds_between":
        measured = result.sessions_measured
        return f"{measured} sesión medida" if measured == 1 else f"{measured} sesiones medidas"
    return ""


def csv_number(value: float) -> int | float:
    """Whole values are written without decimals (a count of 3 is '3', not '3.0')."""
    return int(value) if float(value).is_integer() else round(value, 4)


def write_csv(path: str, config: dict, table: list[tuple[str, dict[str, Result]]]) -> None:
    with open(path, "w", newline="", encoding="utf-8") as out:
        writer = csv.writer(out, lineterminator="\n")
        writer.writerow(["kpi_id", "label", "group", "value", "numerator", "denominator", "sessions_measured"])
        for group, results in table:
            for kpi in config["kpis"]:
                result = results[kpi["id"]]
                writer.writerow([
                    kpi["id"], kpi.get("label", kpi["id"]), group,
                    "" if result.value is None else csv_number(result.value),
                    "" if result.numerator is None else int(result.numerator),
                    "" if result.denominator is None else int(result.denominator),
                    "" if result.sessions_measured is None else result.sessions_measured,
                ])


def to_markdown(config: dict, table: list[tuple[str, dict[str, Result]]]) -> str:
    lines = ["| KPI | Grupo | Valor | Detalle |", "|:--|:--|--:|:--|"]
    for group, results in table:
        for kpi in config["kpis"]:
            result = results[kpi["id"]]
            lines.append(f"| {kpi.get('label', kpi['id'])} | {group} | {format_value(kpi, result)} | {detail(kpi, result)} |")
    return "\n".join(lines) + "\n"


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Calcula los KPI de EXP2 a partir del CSV de eventos.")
    parser.add_argument("--events", required=True, help="CSV exportado con export_usage_events.py")
    parser.add_argument("--config", required=True, help="JSON con las columnas, los eventos y los KPI")
    parser.add_argument("--out", help="escribe los resultados en este CSV")
    parser.add_argument("--markdown", help="escribe la tabla de resultados en este archivo Markdown")
    parser.add_argument("--list-events", action="store_true", help="solo muestra los nombres de evento que hay en el CSV")
    args = parser.parse_args(argv)

    try:
        with open(args.config, encoding="utf-8") as handle:
            config = validate_config(json.load(handle))
        events = load_events(args.events, config["columns"])
    except (ConfigError, json.JSONDecodeError, OSError) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 2

    found = Counter(event.name for event in events)
    if args.list_events:
        for name, count in found.most_common():
            print(f"{count}\t{name}")
        return 0

    missing = sorted(configured_event_names(config) - set(found))
    if missing:
        print("AVISO: estos eventos de la configuración no aparecen en el CSV: " + ", ".join(missing) +
              ". Eventos que sí hay: " + (", ".join(sorted(found)) or "ninguno") + ".", file=sys.stderr)

    table = compute_by_group(config, events)
    if args.out:
        write_csv(args.out, config, table)
    markdown = to_markdown(config, table)
    if args.markdown:
        with open(args.markdown, "w", encoding="utf-8") as out:
            out.write(markdown)
    print(f"{len(events)} eventos leídos.")
    print(markdown)
    return 0


if __name__ == "__main__":
    sys.exit(main())
