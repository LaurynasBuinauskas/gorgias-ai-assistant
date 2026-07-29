using System.Diagnostics;

namespace Copilot.Api.Hosting;

public static class ProblemDetailsExtensions
{
    /// <summary>
    /// Gives failures one shape (RFC 7807) carrying a trace id, so a report of "it broke"
    /// can be tied to a specific request in the logs.
    /// </summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["traceId"] =
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        });

    /// <summary>
    /// Development keeps the developer exception page the host installs, which shows the
    /// stack; everywhere else an unhandled failure returns problem details and nothing more.
    /// </summary>
    public static IApplicationBuilder UseApiExceptionHandler(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler();
        }

        return app;
    }
}
