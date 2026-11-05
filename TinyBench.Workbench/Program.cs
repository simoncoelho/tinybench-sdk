var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "dist"
});

var app = builder.Build();
var distPath = Path.Combine(app.Environment.ContentRootPath, "dist");

if (Directory.Exists(distPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}
else
{
    app.MapGet("/", () => Results.Text(
        "TinyBench Workbench is a React app. Run `npm install` then `npm run dev` in TinyBench.Workbench, or run `npm run build` before using this .NET host.",
        "text/plain"));
}

app.Run();
