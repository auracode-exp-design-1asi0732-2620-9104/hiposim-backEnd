using System.Text;
using HipoSim.Simulations.Reports;
using QuestPDF.Fluent;

namespace HipoSim.Simulations.Tests;

/// <summary>TS03: the PDF rendering. The wording is covered by <see cref="SimulationReportContentBuilderTests"/>.</summary>
public class QuestPdfSimulationReportGeneratorTests
{
    private static async Task<SimulationReportContent> ContentAsync(string json)
    {
        await using var host = await SimulationsTestHost.StartAsync();
        (await host.PostAsync(json)).EnsureSuccessStatusCode();
        return SimulationReportContentBuilder.Build(Assert.Single(host.Repository.Records));
    }

    private const string TwentyYears =
        """{"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":0.085,"termInMonths":240,"graceType":"partial","gracePeriodMonths":6,"applyGoodPayerBonus":true,"startDate":"2026-10-01"}""";

    private const string FiveYears =
        """{"propertyPrice":150000,"downPayment":15000,"annualEffectiveRate":0.09,"termInMonths":60}""";

    [Fact]
    public async Task Generate_WhenTheContentIsValid_ReturnsAWellFormedPdf()
    {
        var generator = new QuestPdfSimulationReportGenerator();

        var pdf = generator.Generate(await ContentAsync(FiveYears));

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Contains("%%EOF", Encoding.Latin1.GetString(pdf, pdf.Length - 16, 16), StringComparison.Ordinal);
        Assert.True(pdf.Length > 5_000, $"The document looks empty ({pdf.Length} bytes).");
    }

    [Fact]
    public async Task Compose_WhenTheScheduleHasSixtyRows_UsesTwoPages()
    {
        var document = new QuestPdfSimulationReportGenerator().Compose(await ContentAsync(FiveYears));

        Assert.Equal(2, document.GenerateImages().Count());
    }

    [Fact]
    public async Task Compose_WhenTheScheduleHasTwoHundredFortyRows_UsesFivePagesAndNoPageHoldsOnlyTheNotes()
    {
        // Checked by eye on the rendered pages: the summary and the notes fill the top of page 1 and the schedule
        // ends on page 5 with installment 240. A sixth page would mean the notes spilled over alone.
        var document = new QuestPdfSimulationReportGenerator().Compose(await ContentAsync(TwentyYears));

        Assert.Equal(5, document.GenerateImages().Count());
    }

    [Fact]
    public async Task Generate_WhenTheSameContentIsRenderedTwice_ProducesTheSamePages()
    {
        var generator = new QuestPdfSimulationReportGenerator();
        var content = await ContentAsync(FiveYears);

        var first = generator.Compose(content).GenerateImages().ToArray();
        var second = generator.Compose(content).GenerateImages().ToArray();

        Assert.Equal(first.Length, second.Length);
        for (var page = 0; page < first.Length; page++)
        {
            Assert.True(first[page].AsSpan().SequenceEqual(second[page]), $"Page {page + 1} differs between two renders.");
        }
    }

    [Fact]
    public void Generate_WhenTheContentIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new QuestPdfSimulationReportGenerator().Generate(null!));
    }
}
