using TinyBench.Sdk.RestHost.Api;
using TinyBench.Sdk.RestHost.Driver;
using TinyBench.Sdk.RestHost.Hosting;

var options = RestHostOptions.Parse(args);
if (options.ShowHelp)
{
    RestHostOptions.PrintHelp();
    return;
}

if (options.DriverAssemblyPath is null)
{
    Console.Error.WriteLine("Missing required --driver-assembly <path>.");
    RestHostOptions.PrintHelp();
    return;
}

var docsPath = options.DocsPath
    ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.DriverAssemblyPath)) ?? ".", ".tinybench");
var loadedDriver = DriverLoader.Load(options.DriverAssemblyPath, docsPath);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTinyBenchRestHost(loadedDriver);

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "TinyBench REST Host v1");
    options.RoutePrefix = "swagger";
});

app.MapTinyBenchApi();
app.Run();
