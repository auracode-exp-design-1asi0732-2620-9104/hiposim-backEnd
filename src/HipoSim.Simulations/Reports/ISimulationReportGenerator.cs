namespace HipoSim.Simulations.Reports;

/// <summary>
/// Renders the report content as a PDF. The endpoint only depends on this seam, so the PDF library can be
/// replaced (the SP01 research is still open) without touching the controller or the access rules.
/// </summary>
public interface ISimulationReportGenerator
{
    /// <summary>The bytes of a PDF document with the report.</summary>
    byte[] Generate(SimulationReportContent content);
}
