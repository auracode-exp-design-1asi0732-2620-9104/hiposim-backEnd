"""Tests for tools/export_usage_events.py and tools/compute_kpis.py (EXP2).

All the data in this file is SYNTHETIC test data written for the tests; none of it comes from real users or from
the real usage_events table.

Run from the repository root:  python -m unittest discover -s tools/tests -v
"""
import contextlib
import csv
import datetime as dt
import decimal
import io
import json
import os
import sys
import tempfile
import unittest
import uuid
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import compute_kpis as kpis  # noqa: E402
import export_usage_events as exporter  # noqa: E402

LIMA = dt.timezone(dt.timedelta(hours=-5))


class FakeCursor:
    def __init__(self, columns, rows):
        self.description = [(name,) for name in columns]
        self._rows = list(rows)
        self.executed = None

    def execute(self, query, params):
        self.executed = (query, list(params))

    def fetchmany(self, size):
        batch, self._rows = self._rows[:size], self._rows[size:]
        return batch

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False


class FakeConnection:
    def __init__(self, columns, rows):
        self.cursor_object = FakeCursor(columns, rows)
        self.closed = False

    def cursor(self):
        return self.cursor_object

    def close(self):
        self.closed = True


class ExporterTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.out = os.path.join(self.directory.name, "events.csv")
        patcher = mock.patch.dict(os.environ, {"DATABASE_URL": "postgresql://user:secret@host/db"})
        patcher.start()
        self.addCleanup(patcher.stop)

    def run_main(self, args, connection):
        calls = []

        def connect(url):
            calls.append(url)
            return connection

        stderr = io.StringIO()
        with contextlib.redirect_stderr(stderr), contextlib.redirect_stdout(io.StringIO()):
            code = exporter.main(["--out", self.out, *args], connect=connect)
        return code, calls, stderr.getvalue()

    def read_out(self):
        with open(self.out, newline="", encoding="utf-8") as handle:
            return handle.read()

    def test_format_value_handles_each_database_type(self):
        moment = dt.datetime(2026, 11, 7, 10, 30, tzinfo=LIMA)
        self.assertEqual("", exporter.format_value(None))
        self.assertEqual("true", exporter.format_value(True))
        self.assertEqual("false", exporter.format_value(False))
        self.assertEqual("-5", exporter.format_value(-5))
        self.assertEqual("1.5", exporter.format_value(1.5))
        self.assertEqual("1.50", exporter.format_value(decimal.Decimal("1.50")))
        self.assertEqual("2026-11-07T10:30:00-05:00", exporter.format_value(moment))
        self.assertEqual("2026-11-07", exporter.format_value(dt.date(2026, 11, 7)))
        self.assertEqual("00000000-0000-0000-0000-000000000001", exporter.format_value(uuid.UUID(int=1)))
        self.assertEqual("0aff", exporter.format_value(bytes([10, 255])))
        self.assertEqual('{"a":1,"b":[true,null]}', exporter.format_value({"b": [True, None], "a": 1}))
        self.assertEqual("plain text", exporter.format_value("plain text"))

    def test_text_that_a_spreadsheet_would_run_as_a_formula_is_neutralized(self):
        for text in ("=HYPERLINK(\"http://x\")", "+1+1", "-2+3", "@SUM(A1)", "\tcmd"):
            self.assertEqual("'" + text, exporter.format_value(text), text)
        self.assertEqual("-5", exporter.format_value(-5), "a negative number stays a number")

    def test_write_csv_quotes_commas_quotes_and_line_breaks(self):
        row = ["a,b", 'he said "hi"', "two\nlines"]
        out = io.StringIO()

        count = exporter.write_csv(["x", "y", "z"], [row], out)

        self.assertEqual(1, count)
        self.assertEqual([["x", "y", "z"], row], list(csv.reader(io.StringIO(out.getvalue()))))

    def test_main_exports_every_column_and_row_and_closes_the_connection(self):
        rows = [
            (1, "simulation_completed", dt.datetime(2026, 11, 7, 15, 0, tzinfo=dt.timezone.utc), {"term": 240}),
            (2, "pdf_downloaded", dt.datetime(2026, 11, 7, 15, 1, tzinfo=dt.timezone.utc), None),
        ]
        connection = FakeConnection(["id", "event_name", "occurred_at", "payload"], rows)

        code, calls, _ = self.run_main([], connection)

        self.assertEqual(0, code)
        self.assertEqual(["postgresql://user:secret@host/db"], calls)
        self.assertEqual(
            "id,event_name,occurred_at,payload\n"
            '1,simulation_completed,2026-11-07T15:00:00+00:00,"{""term"":240}"\n'
            "2,pdf_downloaded,2026-11-07T15:01:00+00:00,\n",
            self.read_out(),
        )
        self.assertTrue(connection.closed)
        query, params = connection.cursor_object.executed
        self.assertEqual([], params)
        self.assertIn("Identifier('usage_events')", repr(query))

    def test_main_passes_the_time_window_as_parameters_in_lima_time(self):
        connection = FakeConnection(["occurred_at"], [])

        code, _, _ = self.run_main(
            ["--time-column", "occurred_at", "--since", "2026-11-07", "--until", "2026-11-11T08:00:00+00:00"],
            connection,
        )

        self.assertEqual(0, code)
        _, params = connection.cursor_object.executed
        self.assertEqual([dt.datetime(2026, 11, 7, tzinfo=LIMA), dt.datetime(2026, 11, 11, 8, tzinfo=dt.timezone.utc)], params)

    def test_main_without_database_url_stops_before_connecting(self):
        with mock.patch.dict(os.environ):
            os.environ.pop("DATABASE_URL")
            code, calls, stderr = self.run_main([], FakeConnection(["a"], []))

        self.assertEqual(2, code)
        self.assertEqual([], calls)
        self.assertIn("DATABASE_URL", stderr)
        self.assertFalse(os.path.exists(self.out))

    def test_main_does_not_overwrite_unless_forced(self):
        with open(self.out, "w", encoding="utf-8") as handle:
            handle.write("previous")

        code, calls, stderr = self.run_main([], FakeConnection(["a"], [(1,)]))
        self.assertEqual(2, code)
        self.assertEqual([], calls)
        self.assertIn("--force", stderr)
        self.assertEqual("previous", self.read_out())

        code, _, _ = self.run_main(["--force"], FakeConnection(["a"], [(1,)]))
        self.assertEqual(0, code)
        self.assertEqual("a\n1\n", self.read_out())

    def test_exclude_columns_removes_them_and_unknown_ones_are_rejected(self):
        code, _, _ = self.run_main(
            ["--exclude-columns", "secret"], FakeConnection(["id", "secret", "event_name"], [(1, "x", "e")]))
        self.assertEqual(0, code)
        self.assertEqual("id,event_name\n1,e\n", self.read_out())

        with self.assertRaises(SystemExit) as raised:
            self.run_main(["--force", "--exclude-columns", "missing"], FakeConnection(["id"], []))
        self.assertIn("missing", str(raised.exception))

    def test_columns_that_look_like_personal_data_are_reported(self):
        connection = FakeConnection(["id", "event_name", "email", "ip_address", "user_id"], [])

        _, _, stderr = self.run_main([], connection)

        self.assertIn("email", stderr)
        self.assertIn("ip_address", stderr)
        self.assertIn("user_id", stderr)
        self.assertNotIn("event_name,", stderr)

    def test_columns_of_a_normal_event_table_are_not_reported(self):
        connection = FakeConnection(["id", "event_name", "occurred_at", "session_id", "source", "payload"], [])

        _, _, stderr = self.run_main([], connection)

        self.assertNotIn("AVISO", stderr)

    def test_invalid_names_and_incomplete_window_are_rejected_before_connecting(self):
        for args in (["--table", "usage_events; drop table x"], ["--table", "a.b.c"], ["--order-by", "id desc"],
                     ["--since", "2026-11-07"], ["--since", "not a date", "--time-column", "t"]):
            with self.subTest(args=args), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as raised:
                    exporter.main(["--out", self.out, *args], connect=lambda url: self.fail("must not connect"))
                self.assertEqual(2, raised.exception.code)


def event(name, session, time, group=""):
    return kpis.Event(name, kpis.parse_time(time, 0), session, group)


# Synthetic events: two Landing sessions (s1, s2) and two mobile sessions (s3, s4).
EVENTS = [
    event("sim_start", "s1", "2026-11-08T10:00:00Z", "landing"),
    event("sim_done", "s1", "2026-11-08T10:00:40Z", "landing"),
    event("pdf", "s1", "2026-11-08T10:01:00Z", "landing"),
    event("contact", "s1", "2026-11-08T10:02:00Z", "landing"),
    event("sim_start", "s2", "2026-11-08T11:00:00Z", "landing"),
    event("sim_done", "s2", "2026-11-08T11:01:30Z", "landing"),
    event("sim_start", "s3", "2026-11-08T12:00:00Z", "mobile"),
    event("sim_start", "s4", "2026-11-08T13:00:00Z", "mobile"),
    event("sim_done", "s4", "2026-11-08T13:00:20Z", "mobile"),
    event("pdf", "s4", "2026-11-08T13:00:30Z", "mobile"),
]

KPI_LIST = [
    {"id": "sessions", "type": "sessions"},
    {"id": "started", "type": "count", "event": "sim_start"},
    {"id": "done", "type": "count", "event": "sim_done"},
    {"id": "rate", "type": "ratio", "numerator": "done", "denominator": "started"},
    {"id": "sessions_done", "type": "sessions", "event": "sim_done"},
    {"id": "sessions_contact", "type": "sessions", "event": "contact"},
    {"id": "conversion", "type": "ratio", "numerator": "sessions_contact", "denominator": "sessions_done"},
    {"id": "pdfs", "type": "count", "event": "pdf"},
    {"id": "first_sim", "type": "median_seconds_between", "to": "sim_done"},
]

COLUMNS = {"event": "event_name", "time": "occurred_at", "session": "session_id", "source": "source"}


class KpiTests(unittest.TestCase):
    def test_totals_are_counted_from_the_events(self):
        results = kpis.compute(KPI_LIST, EVENTS)

        self.assertEqual(4, results["sessions"].value)
        self.assertEqual(4, results["started"].value)
        self.assertEqual(3, results["done"].value)
        self.assertEqual(0.75, results["rate"].value)
        self.assertEqual((3, 4), (results["rate"].numerator, results["rate"].denominator))
        self.assertEqual(3, results["sessions_done"].value)
        self.assertEqual(1, results["sessions_contact"].value)
        self.assertAlmostEqual(1 / 3, results["conversion"].value)
        self.assertEqual(2, results["pdfs"].value)

    def test_median_time_to_first_completed_simulation(self):
        results = kpis.compute(KPI_LIST, EVENTS)

        # Sessions s1, s2 and s4 took 40, 90 and 20 seconds; s3 never finished and is not measured.
        self.assertEqual(40, results["first_sim"].value)
        self.assertEqual(3, results["first_sim"].sessions_measured)

    def test_median_can_start_at_a_named_event(self):
        spec = [{"id": "x", "type": "median_seconds_between", "from": "sim_done", "to": "pdf"}]

        results = kpis.compute(spec, EVENTS)

        # s1: 20 seconds from sim_done to pdf; s4: 10 seconds.
        self.assertEqual(15, results["x"].value)
        self.assertEqual(2, results["x"].sessions_measured)

    def test_end_before_start_is_not_counted(self):
        spec = [{"id": "x", "type": "median_seconds_between", "from": "pdf", "to": "sim_start"}]

        results = kpis.compute(spec, EVENTS)

        self.assertIsNone(results["x"].value)
        self.assertEqual(0, results["x"].sessions_measured)

    def test_groups_are_computed_separately_after_the_total(self):
        config = {"columns": COLUMNS, "kpis": KPI_LIST}

        table = dict(kpis.compute_by_group(config, EVENTS))

        self.assertEqual(["Total", "landing", "mobile"], list(table))
        self.assertEqual(1.0, table["landing"]["rate"].value)
        self.assertEqual(0.5, table["mobile"]["rate"].value)
        self.assertEqual(65, table["landing"]["first_sim"].value)
        self.assertEqual(20, table["mobile"]["first_sim"].value)

    def test_groups_are_listed_alphabetically_whatever_the_order_of_the_events(self):
        names = ["zeta", "alpha", "mid", "beta", "omega"]
        events = [event("e", f"s{index}", "2026-11-08T10:00:00Z", name) for index, name in enumerate(names)]
        config = {"columns": COLUMNS, "kpis": [{"id": "n", "type": "count"}]}

        groups = [group for group, _ in kpis.compute_by_group(config, events)]

        self.assertEqual(["Total", *sorted(names)], groups)

    def test_groups_are_not_added_without_a_source_column(self):
        config = {"columns": {"event": "event_name", "session": "session_id", "time": "occurred_at"}, "kpis": KPI_LIST}

        self.assertEqual(["Total"], [group for group, _ in kpis.compute_by_group(config, EVENTS)])

    def test_ratio_with_zero_denominator_has_no_value(self):
        spec = [
            {"id": "none", "type": "count", "event": "does_not_exist"},
            {"id": "done", "type": "count", "event": "sim_done"},
            {"id": "ratio", "type": "ratio", "numerator": "done", "denominator": "none"},
        ]

        results = kpis.compute(spec, EVENTS)

        self.assertIsNone(results["ratio"].value)
        self.assertEqual("sin datos", kpis.format_value(spec[2], results["ratio"]))

    def test_an_empty_event_list_gives_zeros_and_no_data(self):
        results = kpis.compute(KPI_LIST, [])

        self.assertEqual(0, results["sessions"].value)
        self.assertIsNone(results["rate"].value)
        self.assertIsNone(results["first_sim"].value)

    def test_dates_with_and_without_offset_can_be_compared(self):
        events = [event("a", "s", "2026-11-08T10:00:00"), event("b", "s", "2026-11-08T10:00:05Z")]
        spec = [{"id": "x", "type": "median_seconds_between", "to": "b"}]

        self.assertEqual(5, kpis.compute(spec, events)["x"].value)

    def test_formats_follow_the_type_of_kpi(self):
        results = kpis.compute(KPI_LIST, EVENTS)
        by_id = {kpi["id"]: kpi for kpi in KPI_LIST}

        self.assertEqual("3", kpis.format_value(by_id["done"], results["done"]))
        self.assertEqual("75.0 %", kpis.format_value(by_id["rate"], results["rate"]))
        self.assertEqual("40.0 s", kpis.format_value(by_id["first_sim"], results["first_sim"]))
        self.assertEqual("3 de 4", kpis.detail(by_id["rate"], results["rate"]))
        self.assertEqual("3 sesiones medidas", kpis.detail(by_id["first_sim"], results["first_sim"]))
        self.assertEqual("1 sesión medida", kpis.detail(by_id["first_sim"], kpis.Result(5.0, sessions_measured=1)))

    def test_whole_numbers_are_written_without_decimals_in_the_csv(self):
        self.assertEqual(3, kpis.csv_number(3.0))
        self.assertEqual(0.6667, kpis.csv_number(2 / 3))
        self.assertEqual(40, kpis.csv_number(40.0))

    def test_invalid_configurations_are_explained(self):
        cases = {
            "not an object": [],
            "no event column": {"columns": {}, "kpis": KPI_LIST},
            "no kpis": {"columns": COLUMNS, "kpis": []},
            "no id": {"columns": COLUMNS, "kpis": [{"type": "count"}]},
            "bad type": {"columns": COLUMNS, "kpis": [{"id": "a", "type": "average"}]},
            "duplicate id": {"columns": COLUMNS, "kpis": [{"id": "a", "type": "count"}, {"id": "a", "type": "count"}]},
            "sessions without session column": {"columns": {"event": "e"}, "kpis": [{"id": "a", "type": "sessions"}]},
            "median without time column": {"columns": {"event": "e", "session": "s"},
                                           "kpis": [{"id": "a", "type": "median_seconds_between", "to": "x"}]},
            "median without to": {"columns": COLUMNS, "kpis": [{"id": "a", "type": "median_seconds_between"}]},
            "ratio before its parts": {"columns": COLUMNS,
                                       "kpis": [{"id": "r", "type": "ratio", "numerator": "n", "denominator": "d"}]},
        }
        for name, config in cases.items():
            with self.subTest(name), self.assertRaises(kpis.ConfigError):
                kpis.validate_config(config)

    def test_the_example_configuration_is_valid(self):
        path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "kpi_config.example.json")
        with open(path, encoding="utf-8") as handle:
            config = kpis.validate_config(json.load(handle))

        self.assertTrue(all(value.isupper() or "_" in value for value in config["columns"].values()))
        self.assertTrue(all(name.startswith("EVENTO_") for name in kpis.configured_event_names(config)))


class KpiCommandLineTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.csv_path = self.path("events.csv")
        self.config_path = self.path("config.json")

    def path(self, name):
        return os.path.join(self.directory.name, name)

    def write_csv(self, header, rows, bom=False):
        with open(self.csv_path, "w", newline="", encoding="utf-8-sig" if bom else "utf-8") as handle:
            csv.writer(handle).writerows([header, *rows])

    def write_config(self, config):
        with open(self.config_path, "w", encoding="utf-8") as handle:
            json.dump(config, handle)

    def run_main(self, *extra):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = kpis.main(["--events", self.csv_path, "--config", self.config_path, *extra])
        return code, out.getvalue(), err.getvalue()

    def write_synthetic_events(self, bom=False):
        header = ["event_name", "occurred_at", "session_id", "source"]
        rows = [[e.name, e.time.isoformat(), e.session, e.group] for e in EVENTS]
        self.write_csv(header, rows, bom)

    def test_end_to_end_writes_the_csv_and_the_markdown_table(self):
        self.write_synthetic_events()
        self.write_config({"columns": COLUMNS, "kpis": KPI_LIST})

        code, stdout, stderr = self.run_main("--out", self.path("kpis.csv"), "--markdown", self.path("kpis.md"))

        self.assertEqual(0, code)
        self.assertEqual("", stderr)
        self.assertIn("10 eventos leídos.", stdout)
        with open(self.path("kpis.csv"), newline="", encoding="utf-8") as handle:
            rows = list(csv.DictReader(handle))
        total_rate = next(r for r in rows if r["kpi_id"] == "rate" and r["group"] == "Total")
        self.assertEqual(("0.75", "3", "4"), (total_rate["value"], total_rate["numerator"], total_rate["denominator"]))
        total_done = next(r for r in rows if r["kpi_id"] == "done" and r["group"] == "Total")
        self.assertEqual("3", total_done["value"])
        self.assertEqual(27, len(rows))  # 9 KPIs x (Total + landing + mobile)
        with open(self.path("kpis.md"), encoding="utf-8") as handle:
            self.assertIn("| rate | Total | 75.0 % | 3 de 4 |", handle.read())

    def test_a_csv_saved_with_a_byte_order_mark_is_read(self):
        self.write_synthetic_events(bom=True)
        self.write_config({"columns": COLUMNS, "kpis": KPI_LIST})

        code, stdout, _ = self.run_main()

        self.assertEqual(0, code)
        self.assertIn("10 eventos leídos.", stdout)

    def test_list_events_shows_the_names_found(self):
        self.write_synthetic_events()
        self.write_config({"columns": COLUMNS, "kpis": KPI_LIST})

        code, stdout, _ = self.run_main("--list-events")

        self.assertEqual(0, code)
        self.assertEqual(["4\tsim_start", "3\tsim_done", "2\tpdf", "1\tcontact"], stdout.splitlines())

    def test_events_missing_from_the_csv_are_reported_with_the_ones_that_exist(self):
        self.write_synthetic_events()
        self.write_config({"columns": COLUMNS, "kpis": [{"id": "a", "type": "count", "event": "typo_event"}]})

        code, _, stderr = self.run_main()

        self.assertEqual(0, code)
        self.assertIn("typo_event", stderr)
        self.assertIn("sim_start", stderr)

    def test_a_missing_csv_column_is_explained_with_the_ones_available(self):
        self.write_csv(["name", "when"], [["a", "2026-11-08T10:00:00Z"]])
        self.write_config({"columns": COLUMNS, "kpis": KPI_LIST})

        code, _, stderr = self.run_main()

        self.assertEqual(2, code)
        self.assertIn("event_name", stderr)
        self.assertIn("name, when", stderr)

    def test_an_invalid_date_names_the_line(self):
        self.write_csv(["event_name", "occurred_at", "session_id", "source"], [["a", "yesterday", "s", ""]])
        self.write_config({"columns": COLUMNS, "kpis": KPI_LIST})

        code, _, stderr = self.run_main()

        self.assertEqual(2, code)
        self.assertIn("Línea 2", stderr)

    def test_missing_files_and_broken_json_are_reported_without_a_traceback(self):
        self.write_synthetic_events()
        with open(self.config_path, "w", encoding="utf-8") as handle:
            handle.write("{ not json")

        code, _, stderr = self.run_main()
        self.assertEqual(2, code)
        self.assertIn("Error:", stderr)

        os.remove(self.csv_path)
        self.write_config({"columns": COLUMNS, "kpis": KPI_LIST})
        code, _, stderr = self.run_main()
        self.assertEqual(2, code)
        self.assertIn("Error:", stderr)


if __name__ == "__main__":
    unittest.main()
