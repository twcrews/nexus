using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;
using Nexus.Components;
using Nexus.Providers;
using Nexus.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var keyPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath));

builder.Services.AddMudServices();
builder.Services.AddScoped<SessionTokenStore>();
builder.Services.AddScoped<IDataProvider, AggregateDataProvider>();
builder.Services.AddScoped<RefreshService>();

builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Nexus/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
});

WebApplication app = builder.Build();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var contentRoot = builder.Environment.ContentRootPath;
var privacyHtml = File.ReadAllText(Path.Combine(contentRoot, "Content", "privacy.html"));
var termsHtml = File.ReadAllText(Path.Combine(contentRoot, "Content", "terms.html"));

app.MapGet("/privacy", () => Results.Content(privacyHtml, "text/html"));
app.MapGet("/terms", () => Results.Content(termsHtml, "text/html"));

app.MapGet("/ado-avatar", async (string t, IDataProtectionProvider dpProvider, IHttpClientFactory httpFactory) =>
{
    try
    {
        var protector = dpProvider.CreateProtector("Nexus.AvatarProxy.v1");
        var payload = protector.Unprotect(t);
        var sep = payload.IndexOf('|');
        if (sep < 0) return Results.BadRequest();
        var pat = payload[..sep];
        var imageUrl = payload[(sep + 1)..];

        var client = httpFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}")));

        var response = await client.GetAsync(imageUrl);
        if (!response.IsSuccessStatusCode) return Results.NotFound();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
        return Results.File(bytes, contentType);
    }
    catch
    {
        return Results.NotFound();
    }
});

app.Run();
