using Copilot.Api.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Copilot.Tests;

/// <summary>
/// The admin boundary. The property that matters most: the agents' drafting token must
/// never open <c>/v1/admin</c>, and an unconfigured admin token means the admin surface
/// does not exist — fails closed, not open.
/// </summary>
public sealed class BearerTokenMiddlewareTests
{
    private const string AgentToken = "agent-token";
    private const string AdminToken = "admin-token";

    private static async Task<(int Status, bool ReachedNext)> Run(
        string path,
        string? bearer,
        string adminToken = AdminToken)
    {
        var reachedNext = false;
        var middleware = new BearerTokenMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            Options.Create(new ApiOptions { BearerToken = AgentToken, AdminToken = adminToken }));

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (bearer is not null)
        {
            context.Request.Headers.Authorization = $"Bearer {bearer}";
        }

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, reachedNext);
    }

    [Fact]
    public async Task AgentTokenOpensDraftRoutes()
    {
        var (status, reachedNext) = await Run("/v1/tickets/42/drafts", AgentToken);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(reachedNext);
    }

    [Fact]
    public async Task AgentTokenNeverOpensAdminRoutes()
    {
        var (status, reachedNext) = await Run("/v1/admin/policy/files", AgentToken);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.False(reachedNext);
    }

    [Fact]
    public async Task AdminTokenOpensAdminRoutes()
    {
        var (status, reachedNext) = await Run("/v1/admin/policy/files", AdminToken);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(reachedNext);
    }

    [Fact]
    public async Task AdminTokenDoesNotOpenDraftRoutes()
    {
        // The separation cuts both ways: a leaked admin token should not read tickets.
        var (status, reachedNext) = await Run("/v1/tickets/42/drafts", AdminToken);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.False(reachedNext);
    }

    [Fact]
    public async Task UnconfiguredAdminTokenFailsClosed()
    {
        // An empty expected token must not match an empty provided one.
        var (statusWithEmptyBearer, _) = await Run("/v1/admin/policy/files", "", adminToken: "");
        var (statusWithNoBearer, _) = await Run("/v1/admin/policy/files", null, adminToken: "");

        Assert.Equal(StatusCodes.Status401Unauthorized, statusWithEmptyBearer);
        Assert.Equal(StatusCodes.Status401Unauthorized, statusWithNoBearer);
    }

    [Fact]
    public async Task PublicPathsNeedNoToken()
    {
        var (status, reachedNext) = await Run("/v1/config", null);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(reachedNext);
    }
}
