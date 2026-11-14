#!/usr/bin/env python3
"""Exports the usage_events table to a CSV file (task EXP2).

Read only: the connection is opened in read-only mode and the only statement is a SELECT.
The connection string is taken from the DATABASE_URL environment variable and is never accepted on the
command line, so it does not end up in the terminal history.

The script does not assume the columns of the table (TS09 defines them): it exports whatever the table has.

PowerShell:
    $env:DATABASE_URL = "postgresql://user:password@host:5432/database"
    python tools/export_usage_events.py --out events.csv
    python tools/export_usage_events.py --out events.csv --time-column <column> --since 2026-11-07 --until 2026-11-11
"""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import decimal
import json
import os
import re
import sys
import uuid
from typing import Any, Callable, Iterable, Sequence, TextIO

IDENTIFIER = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
# Column names that suggest personal data. Only a warning: the events must not contain any (TS09).
SENSITIVE_COLUMN = re.compile(
    r"(e-?mail|phone|tel[eé]fono|celular|dni|password|clave|token|(^|_)ip(_|$)|"
    r"(first|last|full)_?name|nombre|apellido|buyer_?id|user_?id)",
    re.IGNORECASE,
)
# A text that starts with one of these is read as a formula by spreadsheet programs.
FORMULA_PREFIXES = ("=", "+", "-", "@", "\t", "\r")
BATCH_SIZE = 1000
LIMA = dt.timezone(dt.timedelta(hours=-5))


def neutralize(text: str) -> str:
    """Prefixes a single quote to text that a spreadsheet would execute as a formula (CSV injection).

    The events endpoint is public, so any text in the table may have been written by a stranger."""
    return "'" + text if text.startswith(FORMULA_PREFIXES) else text


def format_value(value: Any) -> str:
    """Formats one database value for the CSV: ISO 8601 dates, JSON for json columns, empty for NULL."""
    if value is None:
        return ""
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return repr(value)  # numbers are never neutralized: a negative number must stay a number
    if isinstance(value, decimal.Decimal):
        return format(value, "f")
    if isinstance(value, (dt.datetime, dt.date, dt.time)):
        return value.isoformat()
    if isinstance(value, uuid.UUID):
        return str(value)
    if isinstance(value, (bytes, bytearray, memoryview)):
        return bytes(value).hex()
    if isinstance(value, (dict, list)):
        return neutralize(json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True))
    return neutralize(str(value))


def write_csv(columns: Sequence[str], rows: Iterable[Sequence[Any]], out: TextIO) -> int:
    """Writes the header and the rows; returns the number of rows written."""
    writer = csv.writer(out, lineterminator="\n")
    writer.writerow(columns)
    count = 0
    for row in rows:
        writer.writerow([format_value(value) for value in row])
        count += 1
    return count


def parse_bound(text: str) -> dt.datetime:
    """Parses a date or an ISO 8601 date time. Without an offset the time is taken as Lima time (UTC-5)."""
    try:
        value = dt.datetime.fromisoformat(text)
    except ValueError as error:
        raise argparse.ArgumentTypeError(f"'{text}' no es una fecha ISO 8601 (por ejemplo 2026-11-07).") from error
    return value if value.tzinfo else value.replace(tzinfo=LIMA)


def build_query(
    table: str,
    order_by: str | None = None,
    time_column: str | None = None,
    since: dt.datetime | None = None,
    until: dt.datetime | None = None,
):
    """Builds the SELECT with psycopg2.sql so the table and column names are quoted as identifiers."""
    from psycopg2 import sql

    parts = [sql.SQL("SELECT * FROM "), sql.SQL(".").join(sql.Identifier(name) for name in table.split("."))]
    conditions = []
    params: list[Any] = []
    if time_column and since is not None:
        conditions.append(sql.SQL("{} >= %s").format(sql.Identifier(time_column)))
        params.append(since)
    if time_column and until is not None:
        conditions.append(sql.SQL("{} < %s").format(sql.Identifier(time_column)))
        params.append(until)
    if conditions:
        parts += [sql.SQL(" WHERE "), sql.SQL(" AND ").join(conditions)]
    if order_by:
        parts += [sql.SQL(" ORDER BY "), sql.Identifier(order_by)]
    return sql.Composed(parts), params


def export(connection: Any, query: Any, params: Sequence[Any], out_path: str, overwrite: bool,
           exclude: Sequence[str] = ()) -> tuple[list[str], int]:
    """Runs the query and writes the CSV. Returns the exported columns and the number of rows."""
    with connection.cursor() as cursor:
        cursor.execute(query, params)
        all_columns = [description[0] for description in cursor.description]
        unknown = [name for name in exclude if name not in all_columns]
        if unknown:
            raise SystemExit(f"Las columnas a excluir no existen en la tabla: {', '.join(unknown)}. "
                             f"Columnas: {', '.join(all_columns)}")
        keep = [index for index, name in enumerate(all_columns) if name not in exclude]
        columns = [all_columns[index] for index in keep]

        suspicious = [name for name in columns if SENSITIVE_COLUMN.search(name)]
        if suspicious:
            print("AVISO: estas columnas parecen datos personales: " + ", ".join(suspicious) +
                  ". Los eventos no deben llevarlos; use --exclude-columns para quitarlas del CSV.",
                  file=sys.stderr)

        def rows() -> Iterable[list[Any]]:
            for batch in iter(lambda: cursor.fetchmany(BATCH_SIZE), []):
                for row in batch:
                    yield [row[index] for index in keep]

        with open(out_path, "w" if overwrite else "x", newline="", encoding="utf-8") as out:
            count = write_csv(columns, rows(), out)
    return columns, count


def connect_read_only(url: str):
    import psycopg2

    connection = psycopg2.connect(url)
    connection.set_session(readonly=True, autocommit=True)
    return connection


def main(argv: Sequence[str] | None = None, connect: Callable[[str], Any] = connect_read_only) -> int:
    parser = argparse.ArgumentParser(description="Exporta la tabla de eventos de uso a CSV (solo lectura).")
    parser.add_argument("--out", default="usage_events.csv", help="archivo de salida (por defecto usage_events.csv)")
    parser.add_argument("--table", default="usage_events", help="tabla a exportar (por defecto usage_events)")
    parser.add_argument("--time-column", help="columna de fecha, necesaria para --since y --until")
    parser.add_argument("--since", type=parse_bound, help="incluye desde esta fecha (hora de Lima si no trae zona)")
    parser.add_argument("--until", type=parse_bound, help="hasta esta fecha, sin incluirla")
    parser.add_argument("--order-by", help="columna para ordenar el resultado")
    parser.add_argument("--exclude-columns", default="", help="columnas a omitir, separadas por comas")
    parser.add_argument("--force", action="store_true", help="sobrescribe el archivo de salida si ya existe")
    args = parser.parse_args(argv)

    names = args.table.split(".")
    if len(names) > 2 or not all(IDENTIFIER.match(name) for name in names):
        parser.error("--table debe ser un nombre como usage_events o esquema.tabla")
    for option in (args.time_column, args.order_by):
        if option and not IDENTIFIER.match(option):
            parser.error(f"'{option}' no es un nombre de columna válido")
    if (args.since or args.until) and not args.time_column:
        parser.error("--since y --until necesitan --time-column")

    url = os.environ.get("DATABASE_URL")
    if not url:
        print("Falta la variable de entorno DATABASE_URL (en PowerShell: $env:DATABASE_URL = \"postgresql://...\").",
              file=sys.stderr)
        return 2
    if os.path.exists(args.out) and not args.force:
        print(f"{args.out} ya existe; use --force para sobrescribirlo.", file=sys.stderr)
        return 2

    query, params = build_query(args.table, args.order_by, args.time_column, args.since, args.until)
    exclude = [name.strip() for name in args.exclude_columns.split(",") if name.strip()]
    connection = connect(url)
    try:
        columns, count = export(connection, query, params, args.out, args.force, exclude)
    finally:
        connection.close()
    print(f"{count} filas exportadas a {args.out} (columnas: {', '.join(columns)}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
