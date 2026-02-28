// SpeechInsight Blazor WASM host; configures HttpClient to call the API at http://localhost:5200.
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpeechInsight.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:5200") });
builder.Services.AddScoped<SpeechInsight.Client.Services.AudioApiClient>();

await builder.Build().RunAsync();
