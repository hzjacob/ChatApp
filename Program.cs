using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ChatApp;
using MudBlazor.Services;
using Supabase;
using Microsoft.Extensions.DependencyInjection;



var builder = WebAssemblyHostBuilder.CreateDefault(args);

var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseKey = builder.Configuration["Supabase:Key"];
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddHttpClient("ChatAppAPI", client =>
{
    client.BaseAddress = new Uri("http://localhost:5145");
});
builder.Services.AddMudServices();
builder.Services.AddScoped<ChatApp.Services.States>();
builder.Services.AddSingleton( provider =>
    new Supabase.Client(supabaseUrl, supabaseKey, new SupabaseOptions
    {
        AutoConnectRealtime = true
    })
);
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
await builder.Build().RunAsync();
