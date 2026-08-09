using AomrModEditor.Api;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var builder = ApiHostBootstrap.CreateBuilder(args);
var app = ApiHostBootstrap.BuildAndMap(builder);
app.Run();

public partial class Program;
