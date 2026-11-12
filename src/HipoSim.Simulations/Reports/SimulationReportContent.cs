namespace HipoSim.Simulations.Reports;

/// <summary>A label and its already formatted value, for example "Cuota mensual" and "S/ 1,712.30".</summary>
public sealed record ReportField(string Label, string Value);

/// <summary>A titled block of label/value pairs.</summary>
public sealed record ReportSection(string Title, IReadOnlyList<ReportField> Fields);

/// <summary>One amortization row; <see cref="Cells"/> follows the order of <see cref="SimulationReportContent.ScheduleHeaders"/>.</summary>
public sealed record ReportScheduleRow(IReadOnlyList<string> Cells, bool IsGracePeriod);

/// <summary>
/// Everything the PDF shows, as plain formatted text. It is built apart from the PDF library so the wording,
/// the number formats and the rows can be tested without rendering a document.
/// </summary>
public sealed record SimulationReportContent(
    string Title,
    string Subtitle,
    string FileName,
    IReadOnlyList<ReportSection> Sections,
    string ScheduleTitle,
    IReadOnlyList<string> ScheduleHeaders,
    IReadOnlyList<ReportScheduleRow> ScheduleRows,
    IReadOnlyList<string> Notes);
