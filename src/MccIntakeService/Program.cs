using System.Text.Json.Serialization;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Abstractions;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Dispatch;
using MccIntakeService.Application.QualityTests;
using MccIntakeService.Application.Societies;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Configuration;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Wonrich.Auth;
using Wonrich.Auth.Authorization;
using Wonrich.QualityPanel;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container. Enums travel as their names so the API stays readable
// and does not break when a new lifecycle state is inserted.
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Centre operating parameters: daily intake cutoff and the zone its wall clock runs in (SCRUM-6).
builder.Services
    .AddOptions<IntakeOptions>()
    .Bind(builder.Configuration.GetSection(IntakeOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Persistence (SCRUM-36). The connection string is supplied per environment; the local
// development value lives in appsettings.Development.json, which is not committed.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    // Registering the context conditionally used to leave the services that depend on it
    // unsatisfiable, so start-up failed with a DI resolution dump that never mentions the real
    // cause. Fail here instead, naming the setting that is missing.
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Copy "
        + "src/MccIntakeService/appsettings.Development.template.json to appsettings.Development.json "
        + "for local development, or set ConnectionStrings__DefaultConnection in the environment.");
}

builder.Services.AddDbContext<MccIntakeDbContext>(options => options.UseMySQL(connectionString));

builder.Services.AddScoped<IConsignmentReferenceGenerator, ConsignmentReferenceGenerator>();
builder.Services.AddScoped<IConsignmentService, ConsignmentService>();
builder.Services.AddScoped<ISocietyService, SocietyService>();
builder.Services.AddScoped<IQualityTestService, QualityTestService>();
builder.Services.AddScoped<ITankService, TankService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IIntakeClock, IntakeClock>();

// Domain rule violations become ProblemDetails rather than 500s.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// Authentication and authorization (SCRUM-34).
// Tokens are issued by the auth service and validated here independently — signature, issuer,
// audience and expiry — so intake does not call out to authenticate a request. The roles behind
// the policies are the shared Wonrich set, so a token works across every service unchanged.
builder.Services.AddWonrichAuthentication(builder.Configuration);

builder.Services.AddWonrichAuthorization(policies => policies
    .Add(
        IntakePolicies.ManageSocieties,
        WonrichRoles.SystemAdministrator,
        WonrichRoles.MccManager)
    .Add(
        IntakePolicies.RegisterConsignments,
        WonrichRoles.SystemAdministrator,
        WonrichRoles.MccManager,
        WonrichRoles.IntakeOfficer)
    .Add(
        IntakePolicies.RecordQualityTests,
        WonrichRoles.SystemAdministrator,
        WonrichRoles.MccManager,
        WonrichRoles.IntakeOfficer,
        WonrichRoles.QualityAnalyst)
    .Add(
        IntakePolicies.PourToTanks,
        WonrichRoles.SystemAdministrator,
        WonrichRoles.MccManager,
        WonrichRoles.IntakeOfficer)
    .Add(
        // The bowser operator drives; the note is the manager's record of what left the centre.
        IntakePolicies.RecordDispatchNotes,
        WonrichRoles.SystemAdministrator,
        WonrichRoles.MccManager));

// Quality test panel (SCRUM-50). Consumed from the shared library rather than reimplemented,
// so the gate and the lab cannot reach different verdicts on the same readings.
builder.Services.AddQualityPanel(builder.Configuration);

// Swagger / OpenAPI — Swashbuckle (SCRUM-49)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MCC & Intake Service API",
        Version = "v1",
        Description = "Raw milk quality metrics, bowser dispatch notes, and factory-intake condition logging for Wonrich Dairy."
    });

    // Every route on this service is [Authorize], and Swagger UI cannot send a token without a
    // declared scheme, so the endpoints were documented but not exercisable from the page.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Access token from POST /api/auth/login on the auth service. Paste the token only."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });

    // Pick up XML documentation comments
    var xmlFilename = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

app.UseExceptionHandler();

// Auto-apply pending EF migrations in Development and Staging (SCRUM-36).
// Guarded on the provider: the migrations are MySQL-specific, and the integration tests host this
// same pipeline over SQLite, where they cannot be applied and the schema is created directly.
if (!app.Environment.IsProduction())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MccIntakeDbContext>();

    if (db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
    {
        db.Database.Migrate();
    }
}

// Configure the HTTP request pipeline.
// Swagger UI available in Development and Staging; disabled in Production (SCRUM-49)
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MCC & Intake Service v1");
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
