using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.QualityTests;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Configuration;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Infrastructure.Persistence;
using MccIntakeService.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wonrich.QualityPanel;
using Wonrich.QualityPanel.Configuration;

namespace MccIntakeService.Tests.Application;

/// <summary>Covers pouring into a chilling tank and the manifest (SCRUM-52).</summary>
public class TankServiceTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    private readonly FakeIntakeClock _clock = new(new DateTime(2026, 8, 23, 8, 0, 0));

    private TankService CreateService(out MccIntakeDbContext context)
    {
        context = _database.CreateContext();

        return new TankService(context, _clock);
    }

    /// <summary>Registers a consignment and optionally settles a gate verdict against it.</summary>
    private async Task<string> ConsignmentAsync(TestVerdict? verdict, string societyCode = "KC")
    {
        string reference;

        await using (var context = _database.CreateContext())
        {
            var service = new ConsignmentService(
                context,
                new ConsignmentReferenceGenerator(context),
                _clock,
                Options.Create(new IntakeOptions()),
                NullLogger<ConsignmentService>.Instance);

            var society = _database.Society(societyCode);
            var view = await service.RegisterAsync(
                new RegisterConsignmentCommand(society.Id, [new CanEntry(1, 41.2m), new CanEntry(2, 20.6m)]));

            reference = view.Reference;
        }

        if (verdict is null)
        {
            return reference;
        }

        await using var testing = _database.CreateContext();
        await new QualityTestService(
            testing,
            new QualityPanelEvaluator(Options.Create(new QualityThresholds())),
            _clock).RecordAsync(reference, new RecordTestCommand(
                4.1m, 28.5m, 29.0m, 0m, KqColour.Blue,
                new Dictionary<AlcoholStage, StageOutcome> { [AlcoholStage.Alcohol80] = StageOutcome.Negative },
                verdict.Value,
                verdict == TestVerdict.Reject ? "FatPercent" : null,
                verdict == TestVerdict.Reject ? "4.10" : null,
                "officer-1"));

        return reference;
    }

    [Fact]
    public async Task The_centre_has_three_tanks_that_start_empty()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var tanks = await service.ListAsync();

        Assert.Equal(["T1", "T2", "T3"], tanks.Select(tank => tank.Code));
        Assert.All(tanks, tank => Assert.Equal(0m, tank.TotalQuantityLitres));
        Assert.All(tanks, tank => Assert.Equal(0, tank.ConsignmentCount));
    }

    [Fact]
    public async Task An_accepted_consignment_can_be_poured_and_appears_on_the_manifest()
    {
        var reference = await ConsignmentAsync(TestVerdict.Accept);
        var service = CreateService(out var context);
        await using var _ = context;

        var manifest = await service.PourAsync("T1", reference, "officer-2");

        var entry = Assert.Single(manifest.Entries);
        Assert.Equal(reference, entry.ConsignmentReference);
        Assert.Equal("KC", entry.SocietyCode);
        Assert.Equal(["KC 01", "KC 02"], entry.CanLabels);
        Assert.Equal("officer-2", entry.PouredBy);
        Assert.Equal(_clock.UtcNow.UtcDateTime, entry.PouredAtUtc);
    }

    [Fact]
    public async Task The_running_total_updates_on_each_pour()
    {
        var first = await ConsignmentAsync(TestVerdict.Accept);
        var second = await ConsignmentAsync(TestVerdict.Accept, "MT");

        var service = CreateService(out var context);
        await using var _ = context;

        var afterFirst = await service.PourAsync("T1", first, "officer-1");
        var afterSecond = await service.PourAsync("T1", second, "officer-1");

        Assert.Equal(1, afterFirst.Tank.ConsignmentCount);
        Assert.Equal(2, afterSecond.Tank.ConsignmentCount);
        Assert.True(afterSecond.Tank.TotalQuantityLitres > afterFirst.Tank.TotalQuantityLitres);
        Assert.Equal(
            afterSecond.Entries.Sum(entry => entry.QuantityLitres),
            afterSecond.Tank.TotalQuantityLitres);
    }

    [Fact]
    public async Task An_untested_consignment_cannot_be_poured()
    {
        var reference = await ConsignmentAsync(verdict: null);
        var service = CreateService(out var context);
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => service.PourAsync("T1", reference, "officer-1"));

        Assert.Contains("not been tested", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_rejected_consignment_cannot_be_poured()
    {
        var reference = await ConsignmentAsync(TestVerdict.Reject);
        var service = CreateService(out var context);
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => service.PourAsync("T1", reference, "officer-1"));

        Assert.Contains("rejected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_consignment_cannot_be_poured_twice_even_into_a_different_tank()
    {
        var reference = await ConsignmentAsync(TestVerdict.Accept);
        var service = CreateService(out var context);
        await using var _ = context;

        await service.PourAsync("T1", reference, "officer-1");

        await using var second = _database.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => new TankService(second, _clock).PourAsync("T2", reference, "officer-1"));

        Assert.Contains("already been poured", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Only_accepted_and_unpoured_consignments_are_offered()
    {
        var accepted = await ConsignmentAsync(TestVerdict.Accept);
        await ConsignmentAsync(TestVerdict.Reject, "MT");
        await ConsignmentAsync(verdict: null, "NW");

        var service = CreateService(out var context);
        await using var _ = context;

        var before = await service.PourableAsync();
        Assert.Equal(accepted, Assert.Single(before).Reference);

        await service.PourAsync("T1", accepted, "officer-1");

        // Once poured it drops off the list, so it cannot be selected again.
        Assert.Empty(await service.PourableAsync());
    }

    [Fact]
    public async Task The_manifest_can_be_filtered_to_a_single_pour_date()
    {
        var monday = await ConsignmentAsync(TestVerdict.Accept);
        var service = CreateService(out var context);
        await using var _ = context;

        await service.PourAsync("T1", monday, "officer-1");

        var pourDate = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        Assert.Single((await service.ManifestAsync("T1", pourDate))!.Entries);
        Assert.Empty((await service.ManifestAsync("T1", pourDate.AddDays(1)))!.Entries);
    }

    [Fact]
    public async Task A_filtered_manifest_still_reports_the_whole_tank_total()
    {
        var reference = await ConsignmentAsync(TestVerdict.Accept);
        var service = CreateService(out var context);
        await using var _ = context;

        await service.PourAsync("T1", reference, "officer-1");

        var pourDate = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var emptyDay = (await service.ManifestAsync("T1", pourDate.AddDays(1)))!;

        // What the tank holds does not change because of how the manifest is being looked at.
        Assert.Empty(emptyDay.Entries);
        Assert.Equal(1, emptyDay.Tank.ConsignmentCount);
        Assert.True(emptyDay.Tank.TotalQuantityLitres > 0m);
    }

    [Fact]
    public async Task Pours_into_one_tank_do_not_show_on_another()
    {
        var reference = await ConsignmentAsync(TestVerdict.Accept);
        var service = CreateService(out var context);
        await using var _ = context;

        await service.PourAsync("T1", reference, "officer-1");

        Assert.Empty((await service.ManifestAsync("T2"))!.Entries);
        Assert.Equal(0m, (await service.ManifestAsync("T2"))!.Tank.TotalQuantityLitres);
    }

    [Fact]
    public async Task A_tank_code_is_matched_regardless_of_case()
    {
        var reference = await ConsignmentAsync(TestVerdict.Accept);
        var service = CreateService(out var context);
        await using var _ = context;

        await service.PourAsync("t1", reference, "officer-1");

        Assert.Single((await service.ManifestAsync("t1"))!.Entries);
    }

    [Fact]
    public async Task Addressing_a_tank_or_consignment_that_does_not_exist_is_rejected()
    {
        var reference = await ConsignmentAsync(TestVerdict.Accept);
        var service = CreateService(out var context);
        await using var _ = context;

        Assert.Null(await service.ManifestAsync("T9"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.PourAsync("T9", reference, null));
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => service.PourAsync("T1", "MCC-20260823-XX-99", null));
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
