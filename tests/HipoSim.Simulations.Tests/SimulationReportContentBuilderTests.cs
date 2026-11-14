using HipoSim.Simulations.Reports;

namespace HipoSim.Simulations.Tests;

/// <summary>
/// TS03: the text of the report, built from simulations calculated by the real service and engine.
/// The expected values are the ones of the API contract example (price 280,000, down payment 42,000, TEA 8.5%, 240 months).
/// </summary>
public class SimulationReportContentBuilderTests
{
    private const string WithBonus = """
        {"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":0.085,"termInMonths":240,
         "applyGoodPayerBonus":true,"startDate":"2026-10-01"}
        """;

    private static async Task<SimulationRecord> CalculateAsync(string json)
    {
        await using var host = await SimulationsTestHost.StartAsync();
        var response = await host.PostAsync(json);
        response.EnsureSuccessStatusCode();
        return Assert.Single(host.Repository.Records);
    }

    private static string Value(SimulationReportContent content, string label) =>
        content.Sections.SelectMany(section => section.Fields).Single(field => field.Label == label).Value;

    [Fact]
    public async Task Build_WhenTheContractExampleIsCalculated_FormatsTheSummaryInSpanish()
    {
        var content = SimulationReportContentBuilder.Build(await CalculateAsync(WithBonus));

        Assert.Equal("Simulación de crédito hipotecario", content.Title);
        Assert.Equal(["Datos de la simulación", "Resultados", "Supuestos del cálculo"], content.Sections.Select(s => s.Title));
        Assert.Equal("S/ 280,000.00", Value(content, "Precio del inmueble"));
        Assert.Equal("S/ 42,000.00", Value(content, "Cuota inicial"));
        Assert.Equal("S/ 18,200.00", Value(content, "Bono del Buen Pagador"));
        Assert.Equal("S/ 219,800.00", Value(content, "Monto financiado"));
        Assert.Equal("8.50%", Value(content, "Tasa efectiva anual (TEA)"));
        Assert.Equal("240 meses", Value(content, "Plazo"));
        Assert.Equal("Sin periodo de gracia", Value(content, "Periodo de gracia"));
        Assert.Equal("01/10/2026", Value(content, "Fecha de inicio"));
        Assert.Equal("S/ 1,863.99", Value(content, "Cuota mensual"));
        Assert.Equal("8.50%", Value(content, "TCEA"));
    }

    [Fact]
    public async Task Build_WhenTheSimulationIsStored_ShowsItsCodeAndTheLimaTimeInTheSubtitleAndFileName()
    {
        var record = await CalculateAsync(WithBonus);
        // 03:30 UTC is 22:30 of the previous day in Lima (UTC-5).
        record = record with { CreatedAt = new DateTimeOffset(2026, 10, 1, 3, 30, 0, TimeSpan.Zero) };

        var content = SimulationReportContentBuilder.Build(record);

        Assert.Equal($"Código {record.Id} · Calculada el 30/09/2026 22:30 (hora de Lima)", content.Subtitle);
        Assert.Equal($"hiposim-simulacion-{record.Id}.pdf", content.FileName);
    }

    [Fact]
    public async Task Build_WhenTheBonusIsNotRequested_SaysThePropertyIsEligibleButItWasNotApplied()
    {
        var record = await CalculateAsync(
            """{"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":0.085,"termInMonths":240,"applyGoodPayerBonus":false}""");

        var content = SimulationReportContentBuilder.Build(record);

        Assert.Equal("Elegible, no solicitado", Value(content, "Bono del Buen Pagador"));
        Assert.Equal("S/ 238,000.00", Value(content, "Monto financiado"));
    }

    [Fact]
    public async Task Build_WhenThePriceIsOutsideEveryBonusRange_SaysThePropertyIsNotEligible()
    {
        var record = await CalculateAsync(
            """{"propertyPrice":450000,"downPayment":90000,"annualEffectiveRate":0.085,"termInMonths":240,"applyGoodPayerBonus":true}""");

        var content = SimulationReportContentBuilder.Build(record);

        Assert.Equal("No elegible", Value(content, "Bono del Buen Pagador"));
    }

    [Theory]
    [InlineData("total", 3, "Total, 3 meses (los intereses se capitalizan)")]
    [InlineData("partial", 3, "Parcial, 3 meses (se pagan solo los intereses)")]
    [InlineData("partial", 1, "Parcial, 1 mes (se pagan solo los intereses)")]
    public async Task Build_WhenTheLoanHasGrace_DescribesTheTypeAndTheMonths(string graceType, int months, string expected)
    {
        var record = await CalculateAsync(
            $$"""{"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":0.085,"termInMonths":240,"graceType":"{{graceType}}","gracePeriodMonths":{{months}}}""");

        var content = SimulationReportContentBuilder.Build(record);

        Assert.Equal(expected, Value(content, "Periodo de gracia"));
        Assert.Equal(months, content.ScheduleRows.Count(row => row.IsGracePeriod));
        Assert.Contains("Las filas sombreadas corresponden al periodo de gracia.", content.Notes);
    }

    [Theory]
    [InlineData(-17712.02, "-S/ 17,712.02")]
    [InlineData(5, "S/ 5.00")]
    [InlineData(0, "S/ 0.00")]
    public async Task Build_WhenTheNpvIsNegativeOrPositive_PutsTheSignBeforeTheCurrency(double npv, string expected)
    {
        var record = await CalculateAsync(WithBonus);
        record = record with { Response = record.Response with { Npv = npv } };

        var content = SimulationReportContentBuilder.Build(record);

        Assert.Equal(expected, Value(content, "VAN"));
    }

    [Fact]
    public async Task Build_WhenThereIsNoGrace_DoesNotMentionTheShadedRows()
    {
        var content = SimulationReportContentBuilder.Build(await CalculateAsync(WithBonus));

        Assert.DoesNotContain(content.Notes, note => note.Contains("sombreadas", StringComparison.Ordinal));
        Assert.DoesNotContain(content.ScheduleRows, row => row.IsGracePeriod);
    }

    [Fact]
    public async Task Build_WhenTheScheduleIsLong_HasOneRowPerInstallmentWithAsManyCellsAsHeaders()
    {
        var record = await CalculateAsync(WithBonus);

        var content = SimulationReportContentBuilder.Build(record);

        Assert.Equal(240, content.ScheduleRows.Count);
        Assert.Equal(9, content.ScheduleHeaders.Count);
        Assert.All(content.ScheduleRows, row => Assert.Equal(content.ScheduleHeaders.Count, row.Cells.Count));
    }

    [Fact]
    public async Task Build_WhenTheFirstAndLastRowsAreRead_ShowTheEngineValuesFormattedWithThousandsSeparators()
    {
        var record = await CalculateAsync(WithBonus);
        var first = record.Response.AmortizationSchedule[0];
        static string Format(double value) => value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

        var content = SimulationReportContentBuilder.Build(record);

        // Period, date, opening balance, interest, amortization, installment, insurance, total payment, closing balance.
        Assert.Equal(
            ["1", "01/11/2026", "219,800.00", Format(first.Interest), Format(first.Amortization), "1,863.99", "0.00",
             Format(first.TotalPayment), Format(first.RemainingBalance)],
            content.ScheduleRows[0].Cells);

        var last = content.ScheduleRows[^1].Cells;
        Assert.Equal("240", last[0]);
        Assert.Equal("01/10/2046", last[1]);
        Assert.Equal("0.00", last[8]);
    }

    [Fact]
    public async Task Build_WhenInsuranceIsConfigured_ShowsTheSumOfBothInsurancesInTheSegurosColumn()
    {
        await using var host = await SimulationsTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["Simulation:MonthlyPropertyInsurance"] = "20.5",
            ["Simulation:MonthlyCreditLifeInsurance"] = "10.25",
            ["Simulation:AdministrativeExpenses"] = "150",
        });
        (await host.PostAsync(WithBonus)).EnsureSuccessStatusCode();

        var content = SimulationReportContentBuilder.Build(Assert.Single(host.Repository.Records));

        Assert.Equal("30.75", content.ScheduleRows[0].Cells[6]);
        Assert.Equal("S/ 20.50", Value(content, "Seguro del inmueble mensual"));
        Assert.Equal("S/ 10.25", Value(content, "Seguro de desgravamen mensual"));
        Assert.Equal("S/ 150.00", Value(content, "Gastos administrativos"));
    }

    [Fact]
    public async Task Build_WhenAnyReportIsBuilt_UsesNoLongDashesAndEndsWithTheReferentialNotice()
    {
        var content = SimulationReportContentBuilder.Build(await CalculateAsync(WithBonus));

        var text = string.Join('\n', content.Sections.SelectMany(s => s.Fields.Select(f => f.Label + f.Value))
            .Concat(content.Notes).Append(content.Title).Append(content.Subtitle));
        Assert.DoesNotContain((char)0x2014, text); // em dash
        Assert.DoesNotContain((char)0x2013, text); // en dash
        Assert.Equal("Documento referencial generado por HipoSim. No constituye una oferta de crédito.", content.Notes[^1]);
    }

    [Fact]
    public void Build_WhenTheRecordIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SimulationReportContentBuilder.Build(null!));
    }
}
