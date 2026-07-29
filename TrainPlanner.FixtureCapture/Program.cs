using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;
using TrainPlanner.Models;
using TrainPlanner.Services;

// ── Args ──────────────────────────────────────────────────────────────────────
// Usage: dotnet run -- [date] [outputDir]
//   date      : YYYY-MM-DD  (default: today)
//   outputDir : path to write fixture JSON (default: ../TrainPlanner.Tests/Fixtures)
var dateArg   = args.ElementAtOrDefault(0);
var outputArg = args.ElementAtOrDefault(1);

var date = dateArg is not null
    ? DateOnly.Parse(dateArg)
    : DateOnly.FromDateTime(DateTime.Today);

// Default output resolves to the sibling TrainPlanner.Tests/Fixtures folder.
// Runtime path: train-planner/TrainPlanner.FixtureCapture/bin/Debug/net10.0/
// Four levels up = train-planner/  →  then into TrainPlanner.Tests/Fixtures
var outputDir = outputArg ?? Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                 "TrainPlanner.Tests", "Fixtures"));

// ── Config ────────────────────────────────────────────────────────────────────
// Read PlkApiOptions from the main project's appsettings files.
// The tool is a subfolder of train-planner/, so four levels up IS the main project dir.
var mainProjectDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

var config = new ConfigurationBuilder()
    .SetBasePath(mainProjectDir)
    .AddJsonFile("appsettings.json",             optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .Build();

// ── DI ────────────────────────────────────────────────────────────────────────
// Mirror the wiring in train-planner/Program.cs exactly — same options binding,
// same named HttpClient delegate — so PlkTripService behaves identically.
var services = new ServiceCollection();

services.Configure<PlkApiOptions>(config.GetSection(PlkApiOptions.SectionName));

services.AddHttpClient("PlkApi", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<PlkApiOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
    client.DefaultRequestHeaders.Add("X-API-Key", opts.ApiKey);
});

services.AddScoped<IPlkTripService, PlkTripService>();

await using var provider = services.BuildServiceProvider();
await using var scope    = provider.CreateAsyncScope();
var tripService = scope.ServiceProvider.GetRequiredService<IPlkTripService>();

// ── Fetch ─────────────────────────────────────────────────────────────────────
var plkOpts = provider.GetRequiredService<IOptions<PlkApiOptions>>().Value;
Console.WriteLine($"Base URL : {plkOpts.BaseUrl}");
Console.WriteLine($"Date     : {date:yyyy-MM-dd}");
Console.WriteLine($"Output   : {outputDir}");
Console.WriteLine();
Console.WriteLine("Fetching routes...");

var routes = await tripService.GetAllRoutesAsync(date);
Console.WriteLine($"Fetched {routes.Count} routes.");

// ── Serialize ─────────────────────────────────────────────────────────────────
// Wrap in PlkScheduleResponse so the test-side PlkFixture loader can deserialize
// with the same production DTOs and [JsonPropertyName] attributes.
var envelope = new PlkScheduleResponse(
    GeneratedAt:  DateTime.UtcNow,
    Routes:       routes,
    Dictionaries: null);

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var json = JsonSerializer.Serialize(envelope, jsonOptions);

// ── Write ─────────────────────────────────────────────────────────────────────
Directory.CreateDirectory(outputDir);
var outPath = Path.Combine(outputDir, $"{date:yyyy-MM-dd}.json");
await File.WriteAllTextAsync(outPath, json);

Console.WriteLine($"Written  : {outPath}");
