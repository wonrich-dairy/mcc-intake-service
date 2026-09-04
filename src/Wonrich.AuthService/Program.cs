using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Wonrich.Auth;
using Wonrich.Auth.Authorization;
using Wonrich.AuthService.Application;
using Wonrich.AuthService.Controllers;
using Wonrich.AuthService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

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

builder.Services.AddDbContext<AuthDbContext>(options => options.UseMySQL(connectionString));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IUserService, UserService>();

builder.Services
    .AddOptions<SeedOptions>()
    .Bind(builder.Configuration.GetSection(SeedOptions.SectionName));

builder.Services.AddProblemDetails();

// This service both issues and validates tokens: it signs them here, and protects its own
// future administrative endpoints with the same shared validation every other service uses.
builder.Services.AddWonrichAuthentication(builder.Configuration);
// Account administration is the System Administrator's alone (SCRUM-45).
builder.Services.AddWonrichAuthorization(policies => policies
    .Add(AuthPolicies.ManageUsers, WonrichRoles.SystemAdministrator));

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

// Starter accounts for an environment whose user table is empty (SCRUM-45). Never in Production:
// an account whose password is written in configuration is a development convenience, not a way
// to open a real centre.
if (!app.Environment.IsProduction())
{
    using var seedScope = app.Services.CreateScope();
    var seedDb = seedScope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var seedOptions = seedScope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;

    if (seedDb.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
    {
        seedDb.Database.Migrate();
    }

    var seeded = await AuthDbSeeder.SeedAsync(seedDb, seedOptions);

    if (seeded.Count > 0)
    {
        app.Logger.LogInformation(
            "Seeded {Count} starter accounts: {Accounts}", seeded.Count, string.Join(", ", seeded));
    }
}

app.UseCors("frontend");

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
