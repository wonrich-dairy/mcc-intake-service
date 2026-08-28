using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Wonrich.Auth;
using Wonrich.AuthService.Application;
using Wonrich.AuthService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Persistence. The auth service keeps its own database: user credentials must not sit in a
// schema that every other service's connection string can already reach.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Copy "
        + "src/Wonrich.AuthService/appsettings.Development.template.json to "
        + "appsettings.Development.json for local development, or set "
        + "ConnectionStrings__DefaultConnection in the environment.");
}

var serverVersion = builder.Configuration["Database:ServerVersion"];
builder.Services.AddDbContext<AuthDbContext>(options => options.UseMySql(
    connectionString,
    ServerVersion.Parse(string.IsNullOrWhiteSpace(serverVersion) ? "8.0.36-mysql" : serverVersion)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

builder.Services.AddProblemDetails();

// This service both issues and validates tokens: it signs them here, and protects its own
// future administrative endpoints with the same shared validation every other service uses.
builder.Services.AddWonrichAuthentication(builder.Configuration);
builder.Services.AddWonrichAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Wonrich Authentication Service API",
        Version = "v1",
        Description = "Sign-in and token renewal for the Wonrich Dairy services."
    });

    var xmlFilename = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Wonrich Authentication Service v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Exposed so the integration tests can host the application through WebApplicationFactory.</summary>
public partial class Program
{
    protected Program()
    {
    }
}
