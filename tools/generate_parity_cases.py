"""Generates the parity cases for the C# engine by running the original AutoFinance Pro (Python) engine.

Run it with the AutoFinance Pro virtualenv (it needs the project's dependencies), pointing to the project folder
with the AUTOFINANCE_PRO_PATH environment variable or as the first argument:
    <autofinance-pro-backend>\\.venv\\Scripts\\python.exe tools\\generate_parity_cases.py <autofinance-pro-backend>

Rates are written both as the percentage typed in Python ("ratePercent") and as the exact fraction the Python
engine derives from it ("annualRate" = percent / 100), so the C# side starts from the same double.
"""
import json
import os
import sys
from datetime import date
from pathlib import Path

_project = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("AUTOFINANCE_PRO_PATH")
if not _project:
    sys.exit("Pass the AutoFinance Pro folder as the first argument or set AUTOFINANCE_PRO_PATH.")
PYTHON_PROJECT = Path(_project).resolve()
OUTPUT = (
    Path(__file__).resolve().parent.parent
    / "tests" / "HipoSim.FinancialEngine.Tests" / "GoldenData" / "python-parity-cases.json"
)

os.chdir(PYTHON_PROJECT)
sys.path.insert(0, str(PYTHON_PROJECT))

from app.schemas.simulacion import SimulacionInput  # noqa: E402
from app.services.credito_service import CreditoService  # noqa: E402

RATE_TYPES = {"Efectiva": "Effective", "Nominal": "Nominal"}
GRACE_TYPES = {"Ninguno": "None", "Total": "Total", "Parcial": "Partial"}
FREQUENCIES = {
    "Diaria": "Daily", "Mensual": "Monthly", "Bimestral": "Bimonthly", "Trimestral": "Quarterly",
    "Cuatrimestral": "FourMonthly", "Semestral": "Semiannual", "Anual": "Annual",
}


def case(name, price, down, rate_type, rate_pct, term, start, *, freq=None, grace_type="Ninguno",
         grace_months=0, balloon_pct=0.0, benefit=0, prop_ins=0.0, life_ins=0.0, admin=0.0, discount=None):
    return {
        "name": name, "price": price, "down": down, "benefit": benefit, "rate_type": rate_type,
        "rate_pct": rate_pct, "term": term, "start": start, "freq": freq, "grace_type": grace_type,
        "grace_months": grace_months, "balloon_pct": balloon_pct, "prop_ins": prop_ins,
        "life_ins": life_ins, "admin": admin, "discount": discount,
    }


def build_cases():
    cases = [
        # The three documented AutoFinance Pro cases (informe, section 7).
        case("informe-case-1-standard", 68000, 0, "Efectiva", 10.7, 60, date(2026, 5, 14),
             balloon_pct=25, prop_ins=150, life_ins=45),
        case("informe-case-2-partial-grace", 102000, 0, "Efectiva", 11.8, 72, date(2026, 5, 15),
             grace_type="Parcial", grace_months=3, balloon_pct=30, prop_ins=150, life_ins=45),
        case("informe-case-3-total-grace", 24000, 0, "Nominal", 12, 48, date(2026, 5, 15),
             freq="Mensual", grace_type="Total", grace_months=2, balloon_pct=20, prop_ins=150, life_ins=45),
        # Example of the API contract (chapter 5.2.6): 280,000 with 15% down payment and the bonus.
        case("mortgage-contract-example", 280000, 42000, "Efectiva", 8.5, 240, date(2026, 10, 1), benefit=18200),
        case("mortgage-contract-example-no-bonus", 280000, 42000, "Efectiva", 8.5, 240, date(2026, 10, 1)),
    ]

    # Grid of terms x rates x grace.
    index = 0
    for term in (60, 180, 240, 360):
        for rate in (7.0, 9.5):
            for grace_type, grace_months in (("Ninguno", 0), ("Parcial", 6), ("Total", 12)):
                cases.append(case(
                    f"grid-term{term}-rate{rate}-grace{grace_type}",
                    320000, 64000, "Efectiva", rate, term, date(2026, 11, 15),
                    grace_type=grace_type, grace_months=grace_months,
                    prop_ins=[0.0, 28.5][index % 2], life_ins=[0.0, 35.9, 41.25][index % 3],
                    admin=[0.0, 450.0][index % 2], discount=[None, 0.08, 0.12][index % 3],
                ))
                index += 1

    # Every capitalization frequency for a nominal rate.
    for frequency in FREQUENCIES:
        cases.append(case(f"nominal-{frequency}", 300000, 60000, "Nominal", 9.0, 240, date(2026, 9, 10),
                          freq=frequency, life_ins=30.0))

    # Bonus tiers around the Landing Page table.
    for price, benefit in ((180000, 26400), (250000, 18200), (380000, 10000), (450000, 0)):
        cases.append(case(f"bonus-price{price}", price, round(price * 0.10), "Efectiva", 8.9, 300, date(2027, 1, 20),
                          benefit=benefit, prop_ins=22.0, life_ins=31.5, admin=300.0))

    # Optional balloon payment (not used by the mortgage product, kept to validate the original behavior).
    cases.append(case("balloon-10", 300000, 60000, "Efectiva", 9.0, 120, date(2026, 6, 5), balloon_pct=10))
    cases.append(case("balloon-20-partial-grace", 300000, 60000, "Efectiva", 9.0, 120, date(2026, 6, 5),
                      grace_type="Parcial", grace_months=6, balloon_pct=20))
    cases.append(case("balloon-40", 90000, 9000, "Efectiva", 12.0, 48, date(2026, 6, 5), balloon_pct=40))

    # Month-end dates (relativedelta clips to the end of the month and keeps the clipped day).
    cases.append(case("date-jan31", 150000, 30000, "Efectiva", 8.0, 36, date(2026, 1, 31)))
    cases.append(case("date-leap-feb29", 150000, 30000, "Efectiva", 8.0, 48, date(2028, 2, 29)))
    cases.append(case("date-dec31-total-grace", 150000, 30000, "Efectiva", 8.0, 24, date(2026, 12, 31),
                      grace_type="Total", grace_months=3))

    # Extremes: zero rate and a high rate.
    cases.append(case("zero-rate", 120000, 12000, "Efectiva", 0.0, 120, date(2026, 3, 1)))
    cases.append(case("high-rate", 90000, 9000, "Efectiva", 18.5, 60, date(2026, 3, 1),
                      prop_ins=40.0, life_ins=25.0, admin=500.0))
    return cases


def run(c):
    # model_construct skips the vehicle-oriented validations (e.g. 96-month cap) but runs the real service.
    inputs = SimulacionInput.model_construct(
        precio_vehiculo=c["price"], cuota_inicial=c["down"] + c["benefit"], moneda="PEN",
        tasa_interes=c["rate_pct"], tipo_tasa=c["rate_type"], frecuencia_capitalizacion=c["freq"],
        plazo_meses=c["term"], cuota_balon_pct=c["balloon_pct"], tipo_gracia=c["grace_type"],
        meses_gracia=c["grace_months"], seguro_vehicular_mensual=c["prop_ins"],
        seguro_desgravamen_mensual=c["life_ins"], gastos_administrativos=c["admin"],
        tasa_descuento_van=c["discount"], fecha_inicio=c["start"],
    )
    result = CreditoService().simular(inputs)
    ind = result.indicadores
    return {
        "name": c["name"],
        "input": {
            "propertyPrice": c["price"], "downPayment": c["down"], "benefitAmount": c["benefit"],
            "rateType": RATE_TYPES[c["rate_type"]], "ratePercent": c["rate_pct"],
            "annualRate": c["rate_pct"] / 100,
            "capitalizationFrequency": FREQUENCIES[c["freq"]] if c["freq"] else None,
            "termInMonths": c["term"], "startDate": c["start"].isoformat(),
            "graceType": GRACE_TYPES[c["grace_type"]], "graceMonths": c["grace_months"],
            "balloonRatio": c["balloon_pct"] / 100,
            "monthlyPropertyInsurance": c["prop_ins"], "monthlyCreditLifeInsurance": c["life_ins"],
            "administrativeExpenses": c["admin"], "annualDiscountRate": c["discount"],
        },
        "expected": {
            "schedule": [
                [q.numero_cuota, q.fecha_pago.isoformat(), q.cuota, q.interes, q.amortizacion, q.saldo,
                 q.seguro_vehicular, q.seguro_desgravamen, q.total, q.es_periodo_gracia, q.es_cuota_balon]
                for q in result.cronograma
            ],
            "summary": {
                "financedAmount": ind.monto_financiado, "regularInstallment": ind.cuota_regular,
                "balloonAmount": ind.cuota_balon_monto, "totalPaid": ind.total_pagado,
                "totalInterest": ind.total_intereses, "additionalCosts": ind.costos_adicionales,
                "npv": ind.van, "monthlyIrr": ind.tir_mensual, "annualIrr": ind.tir_anual, "tcea": ind.tcea,
            },
        },
    }


def main():
    payload = {
        "source": "AutoFinance Pro (Python) CreditoService.simular",
        "scheduleColumns": [
            "period", "paymentDate", "installment", "interest", "amortization", "remainingBalance",
            "propertyInsurance", "creditLifeInsurance", "totalPayment", "isGracePeriod", "isBalloonPayment",
        ],
        "cases": [run(c) for c in build_cases()],
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
    rows = sum(len(c["expected"]["schedule"]) for c in payload["cases"])
    print(f"{len(payload['cases'])} cases, {rows} schedule rows -> {OUTPUT}")


if __name__ == "__main__":
    main()
