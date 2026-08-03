using Copilot.Api.Contracts;
using Copilot.Pipeline;
using Microsoft.Extensions.Options;

namespace Copilot.Api.Endpoints;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        // IOptionsSnapshot rather than IOptions: read per request, so a configuration reload
        // takes effect without waiting on a process restart.
        app.MapGet("/v1/config", (
            IOptionsSnapshot<ShellConfigOptions> options,
            IOptionsSnapshot<RetrievalOptions> retrieval,
            ILogger<Program> logger) =>
        {
            var shell = options.Value;

            if (shell.KillSwitch)
            {
                // Loud on purpose. If this is engaged, the assistant is off for everyone, and
                // that should be obvious in the logs rather than something to deduce.
                logger.LogWarning("Kill switch is ENGAGED — the shell is mounting no panel");
            }

            return new ConfigResponseV1
            {
                KillSwitch = shell.KillSwitch,
                MinShellVersion = shell.MinShellVersion,
                AnchorProbes = shell.AnchorProbes,
                Exemplars = retrieval.Value.TicketTopK > 0,
            };
        });

        return app;
    }
}
