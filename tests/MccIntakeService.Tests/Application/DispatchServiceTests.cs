using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Dispatch;
using MccIntakeService.Application.QualityTests;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Configuration;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Infrastructure.Persistence;
using MccIntakeService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wonrich.QualityPanel;
using Wonrich.QualityPanel.Configuration;

namespace MccIntakeService.Tests.Application;

/// <summary>Covers bowser dispatch notes and the tank closing they cause (SCRUM-8).</summary>
public class DispatchServiceTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    private readonly FakeIntakeClock _clock = new(new DateTime(2026, 8, 23, 14, 0, 0));

    private DispatchService CreateService(out MccIntakeDbContext context)
    {
        context = _database.CreateContext();

        return new DispatchService(context, _clock);
    }

    /// <summary>Registers a consignment, accepts it at the gate and pours it into a tank.</summary>
    private async Task PourAsync(string tankCode, string societyCode)
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
            reference = (await service.RegisterAsync(
                new RegisterConsignmentCommand(society.Id, [new CanEntry(1, 515m)]))).Reference;
        }

        await using (var testing = _database.CreateContext())
        {
            await new QualityTestService(
                testing,
                new QualityPanelEvaluator(Options.Create(new QualityThresholds())),
                _clock).RecordAsync(reference, new RecordTestCommand(
                    4.1m, 28.5m, 29.0m, 0m, KqColour.Blue,
                    new Dictionary<AlcoholStage, StageOutcome> { [AlcoholStage.Alcohol80] = StageOutcome.Negative },
                    TestVerdict.Accept, null, null, "officer-1"));
        }

        await using var pouring = _database.CreateContext();
        await new TankService(pouring, _clock).PourAsync(tankCode, reference, "officer-1");
    }

    private static RecordDispatchCommand Note(params DispatchDrawCommand[] draws) =>
        new("WP-CAB-1234", "Ranjith Fernando", draws, 4.0m, 8.6m,
            KqColour.Blue, StabilityGrade.Stable, 4.5m, "Morning load", null, "manager-1");

    [Fact]
    public async Task A_note_carries_a_reference_of_the_documented_form()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        var note = await service.RecordAsync(Note(new DispatchDrawCommand("T1", 100m)));

        Assert.Equal("DN-20260823-01", note.Reference);
    }

    [Fact]
    public async Task References_increment_within_a_day()
    {
        await PourAsync("T1", "KC");
        await PourAsync("T2", "MT");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(new DispatchDrawCommand("T1", 100m)));
        var second = await service.RecordAsync(Note(new DispatchDrawCommand("T2", 100m)));

        Assert.Equal("DN-20260823-02", second.Reference);
    }

    [Fact]
    public async Task The_total_is_summed_from_the_per_tank_quantities()
    {
        await PourAsync("T1", "KC");
        await PourAsync("T2", "MT");
        var service = CreateService(out var context);
        await using var _ = context;

        var note = await service.RecordAsync(Note(
            new DispatchDrawCommand("T1", 120.50m),
            new DispatchDrawCommand("T2", 80.25m)));

        Assert.Equal(200.75m, note.TotalQuantityLitres);
        Assert.Equal(2, note.Sources.Count);
    }

    [Fact]
    public async Task A_draw_cannot_exceed_what_the_tank_holds()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => service.RecordAsync(Note(new DispatchDrawCommand("T1", 99_999m))));

        Assert.Contains("cannot be drawn", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Drawing_exactly_what_the_tank_holds_is_allowed()
    {
        await PourAsync("T1", "KC");

        decimal held;

        await using (var reading = _database.CreateContext())
        {
            held = (await new TankService(reading, _clock).ManifestAsync("T1"))!.Tank.TotalQuantityLitres;
        }

        var service = CreateService(out var context);
        await using var _ = context;

        var note = await service.RecordAsync(Note(new DispatchDrawCommand("T1", held)));

        Assert.Equal(held, note.TotalQuantityLitres);
    }

    [Fact]
    public async Task Submitting_a_note_closes_every_tank_it_drew_from()
    {
        await PourAsync("T1", "KC");
        await PourAsync("T2", "MT");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(
            new DispatchDrawCommand("T1", 50m),
            new DispatchDrawCommand("T2", 50m)));

        await using var verification = _database.CreateContext();
        var tanks = await verification.ChillingTanks.ToListAsync();

        Assert.True(tanks.Single(tank => tank.Code == "T1").IsClosed);
        Assert.True(tanks.Single(tank => tank.Code == "T2").IsClosed);
        Assert.False(tanks.Single(tank => tank.Code == "T3").IsClosed);
    }

    [Fact]
    public async Task A_closed_tank_accepts_no_further_pours()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(new DispatchDrawCommand("T1", 50m)));

        // A second consignment cannot follow the bowser out of the door.
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => PourAsync("T1", "MT"));

        Assert.Contains("closed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_closed_tank_cannot_be_dispatched_again()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(new DispatchDrawCommand("T1", 50m)));

        await using var second = _database.CreateContext();
        await Assert.ThrowsAsync<DomainValidationException>(
            () => new DispatchService(second, _clock)
                .RecordAsync(Note(new DispatchDrawCommand("T1", 10m))));
    }

    [Fact]
    public async Task The_note_resolves_to_the_consignments_that_contributed()
    {
        await PourAsync("T1", "KC");
        await PourAsync("T1", "MT");
        var service = CreateService(out var context);
        await using var _ = context;

        var note = await service.RecordAsync(Note(new DispatchDrawCommand("T1", 100m)));

        // This is what lets the factory trace a failure back through the tank to a society.
        var source = Assert.Single(note.Sources);
        Assert.Equal(2, source.ContributingConsignments.Count);
        Assert.All(source.ContributingConsignments, reference => Assert.StartsWith("MCC-", reference));
    }

    [Fact]
    public async Task The_same_tank_cannot_be_listed_twice_on_one_note()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => service.RecordAsync(Note(
                new DispatchDrawCommand("T1", 10m),
                new DispatchDrawCommand("T1", 20m))));

        Assert.Contains("more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_note_must_draw_from_at_least_one_tank()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<DomainValidationException>(() => service.RecordAsync(Note()));
    }

    [Fact]
    public async Task A_zero_or_negative_draw_is_rejected()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<DomainValidationException>(
            () => service.RecordAsync(Note(new DispatchDrawCommand("T1", 0m))));
    }

    [Fact]
    public async Task An_unknown_tank_code_is_rejected()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => service.RecordAsync(Note(new DispatchDrawCommand("T9", 10m))));
    }

    [Fact]
    public async Task Notes_can_be_listed_and_filtered_by_dispatch_date()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(new DispatchDrawCommand("T1", 50m)));

        var day = DateOnly.FromDateTime(_clock.LocalNow);

        Assert.Single(await service.ListAsync(day));
        Assert.Empty(await service.ListAsync(day.AddDays(1)));
        Assert.Single(await service.ListAsync());
    }

    [Fact]
    public async Task A_note_can_be_read_back_with_everything_it_stored()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        var recorded = await service.RecordAsync(Note(new DispatchDrawCommand("T1", 75m)));

        await using var reader = _database.CreateContext();
        var note = await new DispatchService(reader, _clock).GetAsync(recorded.Reference);

        Assert.NotNull(note);
        Assert.Equal("WP-CAB-1234", note.BowserRegistration);
        Assert.Equal("Ranjith Fernando", note.DriverName);
        Assert.Equal(4.0m, note.FatPercent);
        Assert.Equal(8.6m, note.Snf);
        Assert.Equal(nameof(KqColour.Blue), note.KqColour);
        Assert.Equal(nameof(StabilityGrade.Stable), note.StabilityGrade);
        Assert.Equal("Morning load", note.Remarks);
        Assert.Equal("manager-1", note.DispatchedBy);
    }

    [Fact]
    public async Task Reading_back_a_reference_that_does_not_exist_gives_nothing()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        Assert.Null(await service.GetAsync("DN-20260823-99"));
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
