#!/usr/bin/env python3
"""Loads demo data into a HipoSim API through its public endpoints.

It creates fictitious buyers (emails ending in @hiposim.test), simulations with and without grace period,
saved scenarios and leads sent to the demo agency. Optionally, signed in as the demo advisor, it moves some
leads forward and adds follow-up notes, so the advisor web app has something to show.

It never writes usage events: the usage_events table is the evidence of the experiments of chapter VIII and
must only contain real usage.

Everything goes through the API, so the data follows the same validations as the apps, and the script can run
again: an existing buyer is signed in instead of registered.

Passwords are read from environment variables, never from the command line:
    HIPOSIM_DEMO_PASSWORD     password for the demo buyers (8 or more characters, with a letter and a number)
    HIPOSIM_ADVISOR_EMAIL     optional, demo advisor email (Render: Seed__Advisor__Email)
    HIPOSIM_ADVISOR_PASSWORD  optional, demo advisor password (Render: Seed__Advisor__Password)

PowerShell:
    $env:HIPOSIM_DEMO_PASSWORD = "..."
    python tools/seed_demo_data.py --api https://hiposim-api.onrender.com
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request

DEMO_DOMAIN = "hiposim.test"

# Fictitious buyers. Names say "Demo" on purpose so nobody mistakes them for real people.
BUYERS = [
    {"name": "Comprador Demo Uno", "email": "comprador.demo1", "phone": "900000001"},
    {"name": "Comprador Demo Dos", "email": "comprador.demo2", "phone": "900000002"},
    {"name": "Comprador Demo Tres", "email": "comprador.demo3", "phone": "900000003"},
    {"name": "Comprador Demo Cuatro", "email": "comprador.demo4", "phone": "900000004"},
    {"name": "Comprador Demo Cinco", "email": "comprador.demo5", "phone": "900000005"},
    {"name": "Comprador Demo Seis", "email": "comprador.demo6", "phone": "900000006"},
]

RATE = 0.0885

# Per buyer: list of (scenario name, price, down payment, years, bonus, grace type, grace months).
SCENARIOS = [
    [("Depa Surco 20 años", 280000, 42000, 20, True, None, 0),
     ("Depa Surco 25 años", 280000, 42000, 25, True, None, 0)],
    [("Casa Chorrillos con gracia parcial", 350000, 70000, 20, True, "partial", 3),
     ("Casa Chorrillos sin gracia", 350000, 70000, 20, True, None, 0),
     ("Casa Chorrillos 15 años", 350000, 70000, 15, True, None, 0)],
    [("Depa Jesús María", 220000, 33000, 25, True, "total", 2)],
    [("Depa Miraflores", 480000, 96000, 20, False, None, 0),
     ("Depa Miraflores inicial 30 %", 480000, 144000, 20, False, None, 0)],
    [("Primera vivienda SJL", 160000, 16000, 30, True, "partial", 6)],
    [("Depa Lince 20 años", 300000, 45000, 20, True, None, 0),
     ("Depa Lince gracia total", 300000, 45000, 20, True, "total", 3)],
]

# Index of the scenario of each buyer that is sent to the demo agency (None: the buyer does not send any).
LEAD_FROM = [0, 0, 0, None, 0, 1]

# Advisor follow-up for the leads, in order of creation: (new status, note) or None to leave it as New.
FOLLOW_UP = [
    ("InContact", "Primer contacto por teléfono; prefiere visitar el proyecto el fin de semana."),
    ("AppointmentScheduled", "Visita al proyecto agendada. Pidió información de la gracia parcial."),
    None,
    ("InContact", "Se envió por correo la lista de proyectos dentro de su rango de precio."),
    ("Discarded", "Decidió postergar la compra hasta el próximo año."),
]


class ApiError(Exception):
    def __init__(self, status: int, body: str):
        super().__init__(f"HTTP {status}: {body[:300]}")
        self.status = status


def call(api: str, method: str, path: str, body: dict | None = None, token: str | None = None) -> dict | list:
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(api.rstrip("/") + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    request.add_header("Accept", "application/json")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    for attempt in range(3):
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                text = response.read().decode()
                return json.loads(text) if text else {}
        except urllib.error.HTTPError as error:
            raise ApiError(error.code, error.read().decode(errors="replace")) from None
        except urllib.error.URLError:
            if attempt == 2:
                raise
            time.sleep(10)  # the free Render instance may be waking up
    return {}


def sign_in_or_register(api: str, buyer: dict, password: str) -> str:
    email = f"{buyer['email']}@{DEMO_DOMAIN}"
    try:
        result = call(api, "POST", "/api/auth/register", {
            "fullName": buyer["name"], "email": email, "phone": buyer["phone"], "password": password,
            "acceptedTerms": True, "acceptedDataProcessing": True,
        })
        print(f"  registered {email}")
    except ApiError as error:
        if error.status != 409:
            raise
        result = call(api, "POST", "/api/auth/login", {"email": email, "password": password})
        print(f"  signed in {email} (already existed)")
    return result["accessToken"]


def simulate(api: str, token: str, price, down, years, bonus, grace_type, grace_months) -> str:
    body = {
        "propertyPrice": price, "downPayment": down, "annualEffectiveRate": RATE,
        "termInMonths": years * 12, "applyGoodPayerBonus": bonus,
    }
    if grace_type:
        body["graceType"] = grace_type
        body["gracePeriodMonths"] = grace_months
    return call(api, "POST", "/api/simulations", body, token)["simulationId"]


def main() -> int:
    parser = argparse.ArgumentParser(description="Loads demo data through the HipoSim API.")
    parser.add_argument("--api", required=True, help="Base URL of the API, e.g. https://hiposim-api.onrender.com")
    args = parser.parse_args()

    password = os.environ.get("HIPOSIM_DEMO_PASSWORD")
    if not password:
        print("Set HIPOSIM_DEMO_PASSWORD first (8+ characters, with a letter and a number).", file=sys.stderr)
        return 2

    print(f"API: {args.api}")
    print("health:", call(args.api, "GET", "/health"))
    agencies = call(args.api, "GET", "/api/agencies")
    if not agencies:
        print("The API has no agencies: enable Seed__Enabled so the demo agency is created.", file=sys.stderr)
        return 1
    agency_id = agencies[0]["id"]
    print(f"agency: {agencies[0]['name']}")

    totals = {"buyers": 0, "simulations": 0, "scenarios": 0, "leads": 0}
    for index, buyer in enumerate(BUYERS):
        print(f"buyer {index + 1}:")
        token = sign_in_or_register(args.api, buyer, password)
        totals["buyers"] += 1
        existing = {s["name"] for s in call(args.api, "GET", "/api/scenarios", token=token)}
        lead_index = LEAD_FROM[index]
        for position, (name, price, down, years, bonus, grace, months) in enumerate(SCENARIOS[index]):
            if name in existing:
                print(f"  scenario already saved: {name}")
                continue
            simulation_id = simulate(args.api, token, price, down, years, bonus, grace, months)
            totals["simulations"] += 1
            call(args.api, "POST", "/api/scenarios", {"name": name, "simulationId": simulation_id}, token)
            totals["scenarios"] += 1
            print(f"  saved scenario: {name}")
            if lead_index == position:
                call(args.api, "POST", "/api/leads",
                     {"agencyId": agency_id, "simulationId": simulation_id, "acceptedContact": True}, token)
                totals["leads"] += 1
                print("  sent to the demo agency with consent")

    advisor_email = os.environ.get("HIPOSIM_ADVISOR_EMAIL")
    advisor_password = os.environ.get("HIPOSIM_ADVISOR_PASSWORD")
    if advisor_email and advisor_password:
        token = call(args.api, "POST", "/api/auth/login",
                     {"email": advisor_email, "password": advisor_password})["accessToken"]
        leads = call(args.api, "GET", "/api/leads", token=token)
        leads = sorted(leads, key=lambda lead: lead.get("createdAt", ""))
        for lead, follow_up in zip(leads, FOLLOW_UP):
            if follow_up is None or lead.get("status") != "New":
                continue
            status, note = follow_up
            call(args.api, "PATCH", f"/api/leads/{lead['id']}/status", {"status": status}, token)
            call(args.api, "POST", f"/api/leads/{lead['id']}/notes", {"content": note}, token)
            print(f"advisor: lead {lead['id']} -> {status}")
    else:
        print("advisor follow-up skipped (set HIPOSIM_ADVISOR_EMAIL and HIPOSIM_ADVISOR_PASSWORD to add it)")

    print("done:", totals)
    return 0


if __name__ == "__main__":
    sys.exit(main())
