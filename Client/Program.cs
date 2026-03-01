// SpeechInsight Blazor WASM host; configures HttpClient to call the API at http://localhost:5200.
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpeechInsight.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// When served from the API (e.g. Render), same origin; when dev (WASM on :5190), use API origin.
var apiBase = builder.HostEnvironment.IsDevelopment()
    ? "http://localhost:5200"
    : builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBase) });
builder.Services.AddScoped<SpeechInsight.Client.Services.AudioApiClient>();

await builder.Build().RunAsync();
