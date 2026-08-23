using System.Text.Json.Serialization;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Abstractions;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Configuration;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

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
// The server version is configured rather than auto-detected so start-up does not depend on the
// database being reachable, and so migrations can be scaffolded without a running server.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<MccIntakeDbContext>(options =>
        options.UseMySql(connectionString, DatabaseDefaults.ServerVersionFrom(builder.Configuration)));
}

builder.Services.AddScoped<IConsignmentReferenceGenerator, ConsignmentReferenceGenerator>();
builder.Services.AddScoped<IConsignmentService, ConsignmentService>();
builder.Services.AddScoped<ISocietyService, SocietyService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IIntakeClock, IntakeClock>();

// Domain rule violations become ProblemDetails rather than 500s.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

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
