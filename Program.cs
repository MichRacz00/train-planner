using Microsoft.Extensions.Options;
using train_planner.Components;
using train_planner.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Bind PLK API config
builder.Services.Configure<PlkApiOptions>(
    builder.Configuration.GetSection(PlkApiOptions.SectionName));

// Named HttpClient with base URL + API key header
builder.Services.AddHttpClient("PlkApi", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<PlkApiOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
    client.DefaultRequestHeaders.Add("X-API-Key", opts.ApiKey);
});

builder.Services.AddScoped<IPlkTripService, PlkTripService>();
builder.Services.AddScoped<TripService>(); // TODO: remove once UI migrated to IPlkTripService

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();