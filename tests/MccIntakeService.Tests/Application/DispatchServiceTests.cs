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
                    TestVerdict.Accept, TestedBy: "officer-1"));
        }

        await using var pouring = _database.CreateContext();
        await new TankService(pouring, _clock).PourAsync(tankCode, reference, "officer-1");
    }

    /// <summary>What the tank is holding now, read the way the manager's list reads it.</summary>
    private async Task<decimal> HeldAsync(string tankCode)
    {
        await using var context = _database.CreateContext();

        return (await new TankService(context, _clock).ListAsync())
            .Single(tank => tank.Code == tankCode)
            .AvailableQuantityLitres;
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

        var held = await HeldAsync("T1");

        var service = CreateService(out var context);
        await using var _ = context;

        var note = await service.RecordAsync(Note(new DispatchDrawCommand("T1", held)));

        Assert.Equal(held, note.TotalQuantityLitres);
    }

    [Fact]
    public async Task Emptying_a_tank_closes_its_fill_and_opens_the_next()
    {
        await PourAsync("T1", "KC");
        await PourAsync("T2", "MT");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(
            new DispatchDrawCommand("T1", await HeldAsync("T1")),
            new DispatchDrawCommand("T2", await HeldAsync("T2"))));

        await using var verification = _database.CreateContext();
        var tanks = await verification.ChillingTanks.ToListAsync();

        Assert.Equal(2, tanks.Single(tank => tank.Code == "T1").FillNumber);
        Assert.Equal(2, tanks.Single(tank => tank.Code == "T2").FillNumber);
        Assert.NotNull(tanks.Single(tank => tank.Code == "T1").LastClosedAtUtc);

        // A tank nobody drew from is still on its first fill.
        Assert.Equal(1, tanks.Single(tank => tank.Code == "T3").FillNumber);
    }

    [Fact]
    public async Task A_dispatched_tank_can_be_filled_and_dispatched_again()
    {
        // The centre works this cycle every day. Closure that stuck to the tank row rather than
        // to the load would give it three dispatch notes in the lifetime of the database.
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(new DispatchDrawCommand("T1", await HeldAsync("T1"))));

        await PourAsync("T1", "MT");

        await using var second = _database.CreateContext();
        var next = await new DispatchService(second, _clock)
            .RecordAsync(Note(new DispatchDrawCommand("T1", await HeldAsync("T1"))));

        Assert.Equal("DN-20260823-02", next.Reference);
    }

    [Fact]
    public async Task An_emptied_tank_has_nothing_left_for_the_next_bowser()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(new DispatchDrawCommand("T1", await HeldAsync("T1"))));

        await using var second = _database.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => new DispatchService(second, _clock)
                .RecordAsync(Note(new DispatchDrawCommand("T1", 10m))));

        Assert.Contains("holds 0", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Milk_poured_in_after_the_bowser_left_stays_off_the_issued_note()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        var recorded = await service.RecordAsync(
            Note(new DispatchDrawCommand("T1", await HeldAsync("T1"))));

        // The next load starts filling the same tank. The note the factory is already working
        // from has to keep reading exactly as it did when it was issued.
        await PourAsync("T1", "MT");

        await using var reader = _database.CreateContext();
        var note = await new DispatchService(reader, _clock).GetAsync(recorded.Reference);

        var source = Assert.Single(note!.Sources);
        Assert.Single(source.ContributingConsignments);
    }

    [Fact]
    public async Task A_partial_draw_leaves_the_balance_in_the_tank()
    {
        await PourAsync("T1", "KC");
        var held = await HeldAsync("T1");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(new DispatchDrawCommand("T1", 100m)));

        // The fill is not finished, so the tank is not closed and the rest is still drawable.
        await using (var verification = _database.CreateContext())
        {
            Assert.Equal(1, (await verification.ChillingTanks.SingleAsync(tank => tank.Code == "T1")).FillNumber);
        }

        await using var second = _database.CreateContext();
        var next = await new DispatchService(second, _clock)
            .RecordAsync(Note(new DispatchDrawCommand("T1", held - 100m)));

        Assert.Equal(held - 100m, next.TotalQuantityLitres);
    }

    [Fact]
    public async Task A_dispatch_time_in_the_future_is_rejected()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => service.RecordAsync(Note(new DispatchDrawCommand("T1", 50m))
                with { DispatchedAtLocal = _clock.LocalNow.AddYears(4) }));

        Assert.Contains("future", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_dispatch_cannot_predate_the_milk_it_carries()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => service.RecordAsync(Note(new DispatchDrawCommand("T1", 50m))
                with { DispatchedAtLocal = _clock.LocalNow.AddYears(-10) }));

        Assert.Contains("was not filled until", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_tank_list_reports_the_load_the_tank_is_holding_now()
    {
        await PourAsync("T1", "KC");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(new DispatchDrawCommand("T1", await HeldAsync("T1"))));

        await using var reading = _database.CreateContext();
        var tank = (await new TankService(reading, _clock).ListAsync()).Single(view => view.Code == "T1");

        // AC2 has the manager selecting from this list. Reporting milk that has already gone
        // would send them to a 400 at submission.
        Assert.Equal(0m, tank.TotalQuantityLitres);
        Assert.Equal(0m, tank.AvailableQuantityLitres);
        Assert.Equal(0, tank.ConsignmentCount);
        Assert.Equal(2, tank.FillNumber);
        Assert.NotNull(tank.LastClosedAtUtc);
    }

    [Fact]
    public async Task The_tank_list_shows_what_a_partial_draw_left_behind()
    {
        await PourAsync("T1", "KC");
        var held = await HeldAsync("T1");
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(Note(new DispatchDrawCommand("T1", 100m)));

        await using var reading = _database.CreateContext();
        var tank = (await new TankService(reading, _clock).ListAsync()).Single(view => view.Code == "T1");

        Assert.Equal(held, tank.TotalQuantityLitres);
        Assert.Equal(held - 100m, tank.AvailableQuantityLitres);
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
