using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using MccIntakeService.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Entity Framework — MySQL (SCRUM-36)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<MccIntakeDbContext>(options =>
    options.UseMySQL(connectionString ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found. " +
        "Set it in appsettings.json, appsettings.Development.json, or via environment variable.")));

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

// Auto-apply pending EF migrations in Development and Staging (SCRUM-36)
if (!app.Environment.IsProduction())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MccIntakeDbContext>();
    db.Database.Migrate();
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

app.UseAuthorization();

app.MapControllers();

app.Run();
