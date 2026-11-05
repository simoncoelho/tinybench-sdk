using System.Text.Json;
using Microsoft.OpenApi;
using TinyBench.Sdk.RestHost.Driver;
using TinyBench.Sdk.RestHost.Execution;
using TinyBench.Sdk.RestHost.Services;

namespace TinyBench.Sdk.RestHost.Hosting;

public static class RestHostServices
{
    public static IServiceCollection AddTinyBenchRestHost(
        this IServiceCollection services,
        LoadedDriver loadedDriver)
    {
        services.AddSingleton(loadedDriver);
        services.AddSingleton<CommandExecutor>();
        services.AddSingleton<ConnectionInvoker>();
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod());
        });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "TinyBench REST Host",
                Version = "v1",
                Description = "REST wrapper for a TinyBench SDK driver DLL."
            });
        });

        services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.Converters.Add(new TinyTableJsonConverter());
        });

        return services;
    }
}
