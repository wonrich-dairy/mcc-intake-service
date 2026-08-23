using MccIntakeService.Application.Abstractions;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MccIntakeService.Tests.Support;

/// <summary>
/// Hosts the real application pipeline — routing, model validation, the domain exception handler
/// and JSON serialisation — over a SQLite database, so the HTTP contract can be exercised without
/// a MySQL server.
/// </summary>
internal sealed class IntakeApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>The clock the hosted application uses; move it to test cutoff behaviour over HTTP.</summary>
    public FakeIntakeClock Clock { get; } = new(new DateTime(2026, 8, 23, 8, 0, 0));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureServices(services =>
        {
            _connection.Open();

            services.AddDbContext<MccIntakeDbContext>(options => options.UseSqlite(_connection));
            services.AddSingleton<IIntakeClock>(Clock);

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<MccIntakeDbContext>().Database.EnsureCreated();
        });
    }

    /// <summary>Runs an assertion against the database the hosted application is using.</summary>
    public async Task WithDbContextAsync(Func<MccIntakeDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<MccIntakeDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
