using Copilot.Api.Auth;
using Copilot.Api.Endpoints;
using Copilot.Api.RateLimiting;
using Copilot.Gorgias;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGorgias();
builder.Services.AddApiRateLimiting();
builder.Services.AddBearerTokenAuthentication();

var app = builder.Build();

app.UseRateLimiter();
app.UseBearerTokenAuthentication();

app.MapGet("/", () => "Copilot API");
app.MapDraftEndpoints();
app.MapConfigEndpoints();
app.MapTelemetryEndpoints();

app.Run();
