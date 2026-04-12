using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Nexus.Components;
using Nexus.Models;
using Nexus.Providers;
using Nexus.Services;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.Configure<GitHubSettings>(builder.Configuration.GetSection("GitHub"));

builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Nexus/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
});
builder.Services.AddHttpClient("GitHubOAuth", client =>
{
    client.BaseAddress = new Uri("https://github.com/");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/auth/github", (IOptions<GitHubSettings> opts, HttpContext ctx) =>
{
    var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    ctx.Response.Cookies.Append("github_oauth_state", state, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        MaxAge = TimeSpan.FromMinutes(10)
    });
    var url = "https://github.com/login/oauth/authorize" +
              $"?client_id={Uri.EscapeDataString(opts.Value.ClientId)}" +
              $"&scope=repo+read:user&state={Uri.EscapeDataString(state)}" +
              "&prompt=select_account";
    return Results.Redirect(url);
});

app.MapGet("/auth/github/callback", async (
    string code,
    string state,
    HttpContext ctx,
    IOptions<GitHubSettings> opts,
    IHttpClientFactory httpFactory) =>
{
    var savedState = ctx.Request.Cookies["github_oauth_state"];
    ctx.Response.Cookies.Delete("github_oauth_state");
    if (savedState != state)
        return Results.BadRequest("State mismatch.");

    var settings = opts.Value;
    var oauthClient = httpFactory.CreateClient("GitHubOAuth");
    var tokenResp = await oauthClient.PostAsync("login/oauth/access_token",
        JsonContent.Create(new
        {
            client_id = settings.ClientId,
            client_secret = settings.ClientSecret,
            code
        }));
    tokenResp.EnsureSuccessStatusCode();

    var tokenJson = await tokenResp.Content.ReadFromJsonAsync<GitHubTokenResponse>();
    if (tokenJson?.AccessToken is null)
        return Results.BadRequest("Token exchange failed.");

    var apiClient = httpFactory.CreateClient("GitHub");
    apiClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", tokenJson.AccessToken);
    var userJson = await apiClient.GetFromJsonAsync<GitHubUserResponse>("user");
    if (userJson is null)
        return Results.Problem("Failed to fetch GitHub user profile.");

    var token = new GitHubAccountToken
    {
        AccessToken = tokenJson.AccessToken,
        ExpiresAt = DateTimeOffset.MaxValue,
        Login = userJson.Login,
        DisplayName = userJson.Name ?? userJson.Login
    };
    var encoded = Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token)));
    return Results.Redirect($"/link-github?token={Uri.EscapeDataString(encoded)}");
});

app.Run();

file record GitHubTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken);

file record GitHubUserResponse(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("name")] string? Name);
