using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Dispatch;
using MccIntakeService.Application.Factory;
using MccIntakeService.Application.QualityTests;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Configuration;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.Factory;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Infrastructure.Persistence;
using MccIntakeService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wonrich.QualityPanel;
using Wonrich.QualityPanel.Configuration;

namespace MccIntakeService.Tests.Application;

/// <summary>Covers factory arrival screening and batch creation (SCRUM-9).</summary>
public class FactoryIntakeServiceTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    private readonly FakeIntakeClock _clock = new(new DateTime(2026, 8, 23, 16, 0, 0));

    private FactoryIntakeService CreateService(out MccIntakeDbContext context)
    {
        context = _database.CreateContext();

        return new FactoryIntakeService(context, _clock);
    }

    /// <summary>Runs a consignment all the way through to a dispatch note and returns its reference.</summary>
    private async Task<string> DispatchNoteAsync(string tankCode = "T1", string societyCode = "KC")
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

        await using (var pouring = _database.CreateContext())
        {
            await new TankService(pouring, _clock).PourAsync(tankCode, reference, "officer-1");
        }

        await using var dispatching = _database.CreateContext();
        var note = await new DispatchService(dispatching, _clock).RecordAsync(new RecordDispatchCommand(
            "WP-CAB-1234", "Ranjith Fernando",
            [new DispatchDrawCommand(tankCode, 100m)],
            4.0m, 8.6m, KqColour.Blue, StabilityGrade.Stable, 4.5m, null, null, "manager-1"));

        return note.Reference;
    }

    private static ScreenArrivalCommand Screening(
        string dispatchNote,
        bool smell = true,
        bool colour = true,
        bool temperature = true) =>
        new(dispatchNote, smell, colour, temperature, 4.8m, null, "factory-officer-1");

    [Fact]
    public async Task A_clean_screening_creates_a_batch_with_the_documented_reference()
    {
        var note = await DispatchNoteAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        var screening = await service.ScreenAsync(Screening(note));

        Assert.Equal(ScreeningOutcome.Accepted, screening.Outcome);
        Assert.NotNull(screening.Batch);
        Assert.Equal("WR-20260823-01", screening.Batch.Reference);
        Assert.Equal(note, screening.Batch.DispatchNoteReference);
    }

    [Fact]
    public async Task The_batch_stores_the_arrival_time_screening_and_officer()
    {
        var note = await DispatchNoteAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        var screening = await service.ScreenAsync(Screening(note));

        Assert.Equal(_clock.LocalNow, screening.ArrivedAtLocal);
        Assert.Equal(4.8m, screening.TemperatureCelsius);
        Assert.Equal("factory-officer-1", screening.ScreenedBy);
        Assert.Equal("factory-officer-1", screening.Batch!.ScreenedBy);
    }

    [Theory]
    [InlineData(false, true, true, "Smell")]
    [InlineData(true, false, true, "Colour")]
    [InlineData(true, true, false, "Temperature")]
    public async Task A_failure_on_any_parameter_blocks_the_batch_and_names_the_parameter(
        bool smell,
        bool colour,
        bool temperature,
        string expected)
    {
        var note = await DispatchNoteAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        var screening = await service.ScreenAsync(Screening(note, smell, colour, temperature));

        Assert.Equal(ScreeningOutcome.Rejected, screening.Outcome);
        Assert.Null(screening.Batch);
        Assert.Equal(expected, screening.FailedParameters);
    }

    [Fact]
    public async Task Every_failed_parameter_is_recorded_not_just_the_first()
    {
        var note = await DispatchNoteAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        var screening = await service.ScreenAsync(Screening(note, smell: false, colour: false, temperature: false));

        Assert.Equal("Smell, Colour, Temperature", screening.FailedParameters);
    }

    [Fact]
    public async Task A_rejection_is_still_recorded()
    {
        var note = await DispatchNoteAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        await service.ScreenAsync(Screening(note, smell: false));

        // The turn-away has to leave a trail, even though no batch came of it.
        await using var verification = _database.CreateContext();
        var stored = await verification.ArrivalScreenings.SingleAsync();

        Assert.Equal(ScreeningOutcome.Rejected, stored.Outcome);
        Assert.Empty(verification.Batches);
    }

    [Fact]
    public async Task A_rejected_arrival_does_not_burn_a_batch_reference()
    {
        var rejected = await DispatchNoteAsync("T1", "KC");
        var accepted = await DispatchNoteAsync("T2", "MT");

        var service = CreateService(out var context);
        await using var _ = context;

        await service.ScreenAsync(Screening(rejected, smell: false));
        var screening = await service.ScreenAsync(Screening(accepted));

        // The first batch of the day should still be 01; a rejection leaves no gap.
        Assert.Equal("WR-20260823-01", screening.Batch!.Reference);
    }

    [Fact]
    public async Task Batch_references_increment_within_a_day()
    {
        var first = await DispatchNoteAsync("T1", "KC");
        var second = await DispatchNoteAsync("T2", "MT");

        var service = CreateService(out var context);
        await using var _ = context;

        await service.ScreenAsync(Screening(first));
        var next = await service.ScreenAsync(Screening(second));

        Assert.Equal("WR-20260823-02", next.Batch!.Reference);
    }

    [Fact]
    public async Task A_dispatch_note_cannot_be_screened_twice()
    {
        var note = await DispatchNoteAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        await service.ScreenAsync(Screening(note));

        await using var second = _database.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => new FactoryIntakeService(second, _clock).ScreenAsync(Screening(note)));

        Assert.Contains("already been screened", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_rejected_note_cannot_be_re_screened_for_a_second_opinion()
    {
        var note = await DispatchNoteAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        await service.ScreenAsync(Screening(note, smell: false));

        await using var second = _database.CreateContext();
        await Assert.ThrowsAsync<DomainValidationException>(
            () => new FactoryIntakeService(second, _clock).ScreenAsync(Screening(note)));
    }

    [Fact]
    public async Task An_unknown_dispatch_note_is_rejected()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => service.ScreenAsync(Screening("DN-20260823-99")));
    }

    [Fact]
    public async Task A_batch_is_queryable_by_reference_date_and_dispatch_note()
    {
        var note = await DispatchNoteAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        var screening = await service.ScreenAsync(Screening(note));
        var reference = screening.Batch!.Reference;
        var day = DateOnly.FromDateTime(_clock.LocalNow);

        Assert.Equal(reference, (await service.GetBatchAsync(reference))!.Reference);
        Assert.Single(await service.ListBatchesAsync(day));
        Assert.Empty(await service.ListBatchesAsync(day.AddDays(1)));
        Assert.Single(await service.ListBatchesAsync(dispatchNoteReference: note));
        Assert.Empty(await service.ListBatchesAsync(dispatchNoteReference: "DN-20260823-99"));
    }

    [Fact]
    public async Task A_rejected_arrival_never_appears_among_the_batches()
    {
        var note = await DispatchNoteAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        await service.ScreenAsync(Screening(note, colour: false));

        Assert.Empty(await service.ListBatchesAsync());
        Assert.Null(await service.GetBatchAsync("WR-20260823-01"));
    }

    [Fact]
    public async Task An_unknown_batch_reference_gives_nothing()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        Assert.Null(await service.GetBatchAsync("WR-20260823-99"));
    }

    [Fact]
    public void The_checks_report_which_parameters_failed()
    {
        Assert.True(new ScreeningChecks(true, true, true, 4m).AllPassed);
        Assert.Empty(new ScreeningChecks(true, true, true, 4m).FailedParameters);
        Assert.Equal(["Smell", "Temperature"], new ScreeningChecks(false, true, false, 4m).FailedParameters);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
