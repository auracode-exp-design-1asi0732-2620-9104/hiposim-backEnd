using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HipoSim.Simulations.Reports;

/// <summary>
/// QuestPDF implementation of <see cref="ISimulationReportGenerator"/>: A4 portrait, a summary block and the
/// full amortization schedule, whose header repeats on every page.
/// </summary>
/// <remarks>
/// QuestPDF is used under its Community License (free for individuals, academic institutions, open-source projects
/// and organizations under the revenue threshold of its license guide). Review that license before a commercial launch.
/// </remarks>
public sealed class QuestPdfSimulationReportGenerator : ISimulationReportGenerator
{
    private const float PageMargin = 28;
    private const float BodySize = 9;
    private const float TableSize = 7.5f;

    private static readonly string Accent = Colors.Blue.Darken3;
    private static readonly string Muted = Colors.Grey.Darken1;
    private static readonly string Border = Colors.Grey.Lighten2;
    private static readonly string GraceBackground = Colors.Amber.Lighten4;
    private static readonly string HeaderBackground = Colors.Blue.Lighten5;

    static QuestPdfSimulationReportGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(SimulationReportContent content) => Compose(content).GeneratePdf();

    /// <summary>The document before it is rendered; tests use it to count pages and to look at the pages as images.</summary>
    public IDocument Compose(SimulationReportContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(PageMargin);
            page.DefaultTextStyle(style => style.FontSize(BodySize));

            page.Header().Element(header => ComposeHeader(header, content));
            page.Content().Element(body => ComposeBody(body, content));
            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Muted));
                text.Span("HipoSim · Página ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        }));
    }

    private static void ComposeHeader(IContainer container, SimulationReportContent content)
    {
        container.PaddingBottom(10).Column(column =>
        {
            column.Item().Text(content.Title).FontSize(17).Bold().FontColor(Accent);
            column.Item().PaddingTop(2).Text(content.Subtitle).FontSize(8).FontColor(Muted);
        });
    }

    private static void ComposeBody(IContainer container, SimulationReportContent content)
    {
        container.Column(column =>
        {
            column.Spacing(10);

            foreach (var section in content.Sections)
            {
                column.Item().Element(item => ComposeSection(item, section));
            }

            // The notes go before the schedule: after a long table they would end up alone on the last page.
            column.Item().Column(notes =>
            {
                notes.Spacing(2);
                foreach (var note in content.Notes)
                {
                    notes.Item().Text(note).FontSize(8).FontColor(Muted);
                }
            });

            column.Item().Element(item => ComposeSchedule(item, content));
        });
    }

    private static void ComposeSection(IContainer container, ReportSection section)
    {
        container.ShowEntire().Column(column =>
        {
            column.Item().PaddingBottom(3).Text(section.Title).FontSize(11).Bold().FontColor(Accent);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(5);
                    columns.RelativeColumn(6);
                });

                foreach (var field in section.Fields)
                {
                    table.Cell().BorderBottom(0.5f).BorderColor(Border).PaddingVertical(2).Text(field.Label).FontColor(Muted);
                    table.Cell().BorderBottom(0.5f).BorderColor(Border).PaddingVertical(2).AlignRight().Text(field.Value).SemiBold();
                }
            });
        });
    }

    private static void ComposeSchedule(IContainer container, SimulationReportContent content)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(3).Text(content.ScheduleTitle).FontSize(11).Bold().FontColor(Accent);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(24);
                    columns.RelativeColumn(2);
                    for (var i = 2; i < content.ScheduleHeaders.Count; i++)
                    {
                        columns.RelativeColumn(2);
                    }
                });

                table.Header(header =>
                {
                    for (var i = 0; i < content.ScheduleHeaders.Count; i++)
                    {
                        var cell = header.Cell().Background(HeaderBackground).BorderBottom(0.8f).BorderColor(Accent)
                            .PaddingVertical(3).PaddingHorizontal(2);
                        Align(cell, i).Text(content.ScheduleHeaders[i]).FontSize(TableSize).Bold();
                    }
                });

                foreach (var row in content.ScheduleRows)
                {
                    for (var i = 0; i < row.Cells.Count; i++)
                    {
                        var cell = table.Cell().Background(row.IsGracePeriod ? GraceBackground : Colors.White)
                            .BorderBottom(0.4f).BorderColor(Border).PaddingVertical(2).PaddingHorizontal(2);
                        Align(cell, i).Text(row.Cells[i]).FontSize(TableSize);
                    }
                }
            });
        });
    }

    // The first two columns (period and date) are centered; every amount is right aligned.
    private static IContainer Align(IContainer cell, int column) =>
        column < 2 ? cell.AlignCenter() : cell.AlignRight();
}
