var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Copilot_Api>("api");

// Aspire assigns the panel's port via the PORT env var; vite.config.ts honors it
// (and falls back to 5173 when the panel runs standalone via `pnpm dev`).
builder.AddViteApp("panel", "../panel")
    .WithPnpm()
    .WithEnvironment("VITE_API_URL", api.GetEndpoint("http"));

builder.Build().Run();
