using Nexus.Components;
using Nexus.Providers;
using Nexus.Services;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRadzenComponents();
builder.Services.AddScoped<SessionTokenStore>();
builder.Services.AddScoped<IDataProvider>(sp =>
{
    var session = sp.GetRequiredService<SessionTokenStore>();
    var accounts = session.GetLinkedAccounts();

    // Real providers will be constructed here from accounts.MicrosoftAccounts
    // and accounts.GitHubAccounts once ADO/GitHub providers are implemented.
    List<IDataProvider> providers =
    [
        new DummyProvider(), // remove when real providers are in place
    ];

    return new AggregateDataProvider(providers);
});
builder.Services.AddScoped<RefreshService>();

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

app.Run();
