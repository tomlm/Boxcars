namespace Boxcars.Services.Reports;

public static class ReportEndpoints
{
    public const string RailroadReportPath = "/reports/railroads.pdf";

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /reports/railroads.pdf?gameId=abc -> railroad and probability reference for that game's map
        // (the default map without a gameId). Served without a download name so the browser shows it inline.
        endpoints.MapGet(RailroadReportPath, async (string? gameId, RailroadReportService reportService, CancellationToken cancellationToken) =>
        {
            var pdf = await reportService.CreatePdfAsync(gameId, cancellationToken);
            return pdf is null
                ? Results.NotFound()
                : Results.File(pdf, "application/pdf");
        })
        .RequireAuthorization();

        return endpoints;
    }

    public static string BuildRailroadReportUrl(string? gameId)
    {
        return string.IsNullOrWhiteSpace(gameId)
            ? RailroadReportPath
            : $"{RailroadReportPath}?gameId={Uri.EscapeDataString(gameId)}";
    }
}
