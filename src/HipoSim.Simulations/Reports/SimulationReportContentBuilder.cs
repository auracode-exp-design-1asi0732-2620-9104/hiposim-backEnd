using System.Globalization;
using HipoSim.FinancialEngine;

namespace HipoSim.Simulations.Reports;

/// <summary>
/// Turns a persisted simulation into the text of the report. The wording is Spanish (the users are in Peru);
/// numbers use the invariant culture on purpose so the output does not depend on the ICU data of the host:
/// "1,234.56" for money (the Peruvian convention) and "dd/MM/yyyy" for dates.
/// </summary>
public static class SimulationReportContentBuilder
{
    // Peru does not observe daylight saving time, so Lima is always UTC-5.
    private static readonly TimeSpan LimaOffset = TimeSpan.FromHours(-5);

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static readonly IReadOnlyList<string> ScheduleHeaders =
    [
        "N°", "Fecha de pago", "Saldo inicial", "Interés", "Amortización", "Cuota", "Seguros", "Pago total", "Saldo final",
    ];

    public static SimulationReportContent Build(SimulationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var input = record.Input;
        var response = record.Response;
        var hasGrace = input.GraceType != GraceType.None && input.GraceMonths > 0;

        var data = new ReportSection("Datos de la simulación",
        [
            new("Precio del inmueble", Money(input.PropertyPrice)),
            new("Cuota inicial", Money(input.DownPayment)),
            new("Bono del Buen Pagador", Benefit(response.Benefit)),
            new("Monto financiado", Money(response.FinancedAmount)),
            new("Tasa efectiva anual (TEA)", Percent(input.AnnualRate)),
            new("Plazo", Months(input.TermInMonths)),
            new("Periodo de gracia", Grace(input.GraceType, input.GraceMonths)),
            new("Fecha de inicio", Date(input.StartDate)),
        ]);

        var results = new ReportSection("Resultados",
        [
            new("Cuota mensual", Money(response.MonthlyInstallment)),
            new("Total pagado", Money(response.TotalPaid)),
            new("Total de intereses", Money(response.TotalInterest)),
            new("Costos adicionales", Money(response.AdditionalCosts)),
            new("TCEA", Percent(response.Tcea)),
            new("TIR anual", Percent(response.Irr)),
            new("VAN", Money(response.Npv)),
        ]);

        var assumptions = new ReportSection("Supuestos del cálculo",
        [
            new("Seguro del inmueble mensual", Money(response.Assumptions.MonthlyPropertyInsurance)),
            new("Seguro de desgravamen mensual", Money(response.Assumptions.MonthlyCreditLifeInsurance)),
            new("Gastos administrativos", Money(response.Assumptions.AdministrativeExpenses)),
            new("Tasa de descuento anual (VAN)", Percent(response.Assumptions.AnnualDiscountRate)),
        ]);

        var rows = response.AmortizationSchedule
            .Select(entry => new ReportScheduleRow(
            [
                entry.Period.ToString(Invariant),
                Date(entry.PaymentDate),
                Number(entry.OpeningBalance),
                Number(entry.Interest),
                Number(entry.Amortization),
                Number(entry.Installment),
                Number(entry.PropertyInsurance + entry.CreditLifeInsurance),
                Number(entry.TotalPayment),
                Number(entry.RemainingBalance),
            ],
            entry.IsGracePeriod))
            .ToArray();

        var notes = new List<string>
        {
            "Cronograma calculado con el método francés (cuotas constantes). Los montos están en soles (S/).",
            "La TCEA se calcula con los flujos del cronograma, seguros incluidos.",
        };
        if (hasGrace)
        {
            notes.Add("Las filas sombreadas corresponden al periodo de gracia.");
        }

        notes.Add("Documento referencial generado por HipoSim. No constituye una oferta de crédito.");

        return new SimulationReportContent(
            Title: "Simulación de crédito hipotecario",
            Subtitle: $"Código {record.Id:D} · Calculada el {Timestamp(record.CreatedAt)} (hora de Lima)",
            FileName: $"hiposim-simulacion-{record.Id:D}.pdf",
            Sections: [data, results, assumptions],
            ScheduleTitle: "Cronograma de pagos",
            ScheduleHeaders: ScheduleHeaders,
            ScheduleRows: rows,
            Notes: notes);
    }

    // The sign goes before the currency: "-S/ 17,712.02".
    private static string Money(double value) => (value < 0 ? "-S/ " : "S/ ") + Number(Math.Abs(value));

    private static string Number(double value) => value.ToString("N2", Invariant);

    private static string Percent(double fraction) => (fraction * 100).ToString("0.00", Invariant) + "%";

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Invariant);

    private static string Timestamp(DateTimeOffset moment) =>
        moment.ToOffset(LimaOffset).ToString("dd/MM/yyyy HH:mm", Invariant);

    private static string Months(int months) => months == 1 ? "1 mes" : $"{months.ToString(Invariant)} meses";

    private static string Benefit(BenefitResponse benefit)
    {
        if (benefit.AppliedAmount > 0)
        {
            return Money(benefit.AppliedAmount);
        }

        return benefit.Eligible ? "Elegible, no solicitado" : "No elegible";
    }

    private static string Grace(GraceType type, int months)
    {
        if (type == GraceType.None || months <= 0)
        {
            return "Sin periodo de gracia";
        }

        return type == GraceType.Total
            ? $"Total, {Months(months)} (los intereses se capitalizan)"
            : $"Parcial, {Months(months)} (se pagan solo los intereses)";
    }
}
