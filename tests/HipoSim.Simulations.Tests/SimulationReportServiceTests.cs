using HipoSim.Simulations.Reports;

namespace HipoSim.Simulations.Tests;

/// <summary>TS03 access rules, isolated from HTTP and from the PDF library.</summary>
public class SimulationReportServiceTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherBuyerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FakeGenerator : ISimulationReportGenerator
    {
        public int Calls { get; private set; }

        public byte[] Generate(SimulationReportContent content)
        {
            Calls++;
            return [0x25, 0x50, 0x44, 0x46]; // "%PDF"
        }
    }

    private static async Task<(SimulationReportService Service, FakeGenerator Generator, SimulationRecord Record)> ArrangeAsync(Guid? ownerId)
    {
        await using var host = await SimulationsTestHost.StartAsync();
        (await host.PostAsync(
            """{"propertyPrice":280000,"downPayment":42000,"annualEffectiveRate":0.085,"termInMonths":240}""",
            ownerId?.ToString())).EnsureSuccessStatusCode();
        var record = Assert.Single(host.Repository.Records);
        Assert.Equal(ownerId, record.BuyerId);

        var repository = new RecordingRepository();
        await repository.AddAsync(record);
        var generator = new FakeGenerator();
        return (new SimulationReportService(repository, generator), generator, record);
    }

    [Fact]
    public async Task GetReport_WhenTheIdIsUnknown_ReturnsNotFoundWithoutGeneratingAnything()
    {
        var (service, generator, _) = await ArrangeAsync(ownerId: null);

        var result = await service.GetReportAsync(Guid.NewGuid(), buyerId: null);

        Assert.Equal(SimulationReportStatus.NotFound, result.Status);
        Assert.Null(result.Pdf);
        Assert.Equal(0, generator.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetReport_WhenTheSimulationHasNoBuyer_AnyCallerReceivesThePdf(bool callerIsSignedIn)
    {
        var (service, generator, record) = await ArrangeAsync(ownerId: null);

        var result = await service.GetReportAsync(record.Id, callerIsSignedIn ? OtherBuyerId : null);

        Assert.Equal(SimulationReportStatus.Ready, result.Status);
        Assert.NotEmpty(result.Pdf!);
        Assert.Equal($"hiposim-simulacion-{record.Id}.pdf", result.FileName);
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task GetReport_WhenTheCallerIsTheOwner_ReturnsThePdf()
    {
        var (service, generator, record) = await ArrangeAsync(OwnerId);

        var result = await service.GetReportAsync(record.Id, OwnerId);

        Assert.Equal(SimulationReportStatus.Ready, result.Status);
        Assert.Equal(1, generator.Calls);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("anonymous")]
    public async Task GetReport_WhenTheSimulationBelongsToABuyerAndTheCallerIsNotThatBuyer_ReturnsForbidden(string caller)
    {
        var (service, generator, record) = await ArrangeAsync(OwnerId);

        var result = await service.GetReportAsync(record.Id, caller == "other" ? OtherBuyerId : null);

        Assert.Equal(SimulationReportStatus.Forbidden, result.Status);
        Assert.Null(result.Pdf);
        Assert.Null(result.FileName);
        Assert.Equal(0, generator.Calls);
    }
}
