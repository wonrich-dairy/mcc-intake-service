using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.QualityTests;
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

/// <summary>Covers gate quality testing and its verdict rules (SCRUM-7).</summary>
public class QualityTestServiceTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    private readonly FakeIntakeClock _clock = new(new DateTime(2026, 8, 23, 8, 0, 0));

    private QualityTestService CreateService(out MccIntakeDbContext context)
    {
        context = _database.CreateContext();

        return new QualityTestService(
            context,
            new QualityPanelEvaluator(Options.Create(new QualityThresholds())),
            _clock);
    }

    /// <summary>Registers a consignment so there is something to test.</summary>
    private async Task<string> RegisterConsignmentAsync()
    {
        await using var context = _database.CreateContext();

        var service = new ConsignmentService(
            context,
            new ConsignmentReferenceGenerator(context),
            _clock,
            Options.Create(new IntakeOptions()),
            NullLogger<ConsignmentService>.Instance);

        var society = _database.Society("KC");
        var view = await service.RegisterAsync(
            new RegisterConsignmentCommand(society.Id, [new CanEntry(1, 41.2m)]));

        return view.Reference;
    }

    /// <summary>Readings that comfortably pass, with the cascade negative at 80%.</summary>
    private static RecordTestCommand SoundPanel(
        TestVerdict verdict = TestVerdict.Accept,
        decimal fat = 4.1m,
        decimal raw = 28.5m,
        decimal temperature = 29.0m,
        decimal water = 0m,
        KqColour kq = KqColour.Blue,
        string? failedParameter = null,
        string? failedValue = null,
        SensoryCheck? sensory = null) =>
        new(fat, raw, temperature, water, kq,
            new Dictionary<AlcoholStage, StageOutcome> { [AlcoholStage.Alcohol80] = StageOutcome.Negative },
            verdict,
            Sensory: sensory,
            FailedParameter: failedParameter,
            FailedValue: failedValue,
            TestedBy: "officer-1");

    /// <summary>Readings whose cascade clots all the way through boiling.</summary>
    private static RecordTestCommand CurdledPanel(TestVerdict verdict) =>
        new(4.1m, 28.5m, 29.0m, 0m, KqColour.Blue,
            new Dictionary<AlcoholStage, StageOutcome>
            {
                [AlcoholStage.Alcohol80] = StageOutcome.Positive,
                [AlcoholStage.Alcohol75] = StageOutcome.Positive,
                [AlcoholStage.Alcohol68] = StageOutcome.Positive,
                [AlcoholStage.ClotOnBoiling] = StageOutcome.Positive
            },
            verdict,
            FailedParameter: "Stability",
            FailedValue: "Curdled",
            TestedBy: "officer-1");

    [Fact]
    public void The_preview_derives_clr_snf_and_ts_before_anything_is_recorded()
    {
        var service = CreateService(out var context);
        using var _ = context;

        var preview = service.Preview(SoundPanel());

        // Raw 28.5 at 29 °C corrects to 28.90; SNF = 0.902 + 7.225 + 0.72 = 8.85; TS = 12.95.
        Assert.Equal(28.90m, preview.CorrectedClr);
        Assert.Equal(8.85m, preview.Snf);
        Assert.Equal(12.95m, preview.TotalSolids);
        Assert.True(preview.MeetsStandard);
        Assert.False(preview.ClotOnBoiling);
    }

    [Fact]
    public void The_preview_highlights_every_measure_outside_its_threshold()
    {
        var service = CreateService(out var context);
        using var _ = context;

        var preview = service.Preview(SoundPanel(fat: 1.0m, raw: 20.0m, water: 9.0m, kq: KqColour.White));

        Assert.False(preview.MeetsStandard);
        Assert.Contains(preview.Measures, m => m.Measure == "FatPercent" && m.IsOutsideThreshold);
        Assert.Contains(preview.Measures, m => m.Measure == "WaterPercent" && m.IsOutsideThreshold);
        Assert.Contains(preview.Measures, m => m.Measure == "KqColour" && m.IsOutsideThreshold);

        // Each highlighted measure says why, so the officer can act on it at entry time.
        Assert.All(
            preview.Measures.Where(m => m.IsOutsideThreshold),
            m => Assert.False(string.IsNullOrWhiteSpace(m.Detail)));
    }

    [Fact]
    public void The_preview_reports_the_water_reading_rather_than_another_measure()
    {
        var service = CreateService(out var context);
        using var _ = context;

        var preview = service.Preview(SoundPanel(fat: 4.1m, water: 3.25m));

        Assert.Equal("3.25", Assert.Single(preview.Measures, m => m.Measure == "WaterPercent").Value);
    }

    [Fact]
    public void The_preview_records_nothing()
    {
        var service = CreateService(out var context);
        using var _ = context;

        service.Preview(SoundPanel());

        using var verification = _database.CreateContext();
        Assert.Empty(verification.QualityTests);
    }

    [Fact]
    public async Task Recording_a_pass_stores_the_panel_and_accepts_the_consignment()
    {
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        var view = await service.RecordAsync(reference, SoundPanel());

        Assert.Equal(TestVerdict.Accept, view.Verdict);
        Assert.Equal(28.90m, view.CorrectedClr);
        Assert.Equal(8.85m, view.Snf);
        Assert.Equal("officer-1", view.TestedBy);

        await using var verification = _database.CreateContext();
        var consignment = await verification.Consignments.SingleAsync(c => c.Reference == reference);
        Assert.Equal(ConsignmentStatus.Accepted, consignment.Status);
    }

    [Fact]
    public async Task Recording_a_rejection_turns_the_consignment_away()
    {
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(reference, SoundPanel(
            TestVerdict.Reject, fat: 1.0m, failedParameter: "FatPercent", failedValue: "1.00"));

        await using var verification = _database.CreateContext();
        var consignment = await verification.Consignments.SingleAsync(c => c.Reference == reference);
        Assert.Equal(ConsignmentStatus.Rejected, consignment.Status);
    }

    [Fact]
    public async Task A_rejection_must_name_the_failed_parameter_and_its_value()
    {
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<DomainValidationException>(
            () => service.RecordAsync(reference, SoundPanel(TestVerdict.Reject)));

        await Assert.ThrowsAsync<DomainValidationException>(
            () => service.RecordAsync(reference, SoundPanel(
                TestVerdict.Reject, failedParameter: "Snf")));
    }

    [Fact]
    public async Task A_clot_on_boiling_cannot_be_accepted()
    {
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => service.RecordAsync(reference, CurdledPanel(TestVerdict.Accept)));

        Assert.Contains("clotted on boiling", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_clot_on_boiling_is_recorded_as_a_rejection()
    {
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        var view = await service.RecordAsync(reference, CurdledPanel(TestVerdict.Reject));

        Assert.Equal(TestVerdict.Reject, view.Verdict);
        Assert.Equal(nameof(StabilityGrade.Curdled), view.StabilityGrade);
        Assert.Equal(4, view.AlcoholStages.Count);
    }

    [Fact]
    public async Task A_consignment_cannot_be_tested_twice()
    {
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(reference, SoundPanel());

        await using var second = _database.CreateContext();
        var again = new QualityTestService(
            second,
            new QualityPanelEvaluator(Options.Create(new QualityThresholds())),
            _clock);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => again.RecordAsync(reference, SoundPanel()));

        Assert.Contains("already been tested", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Testing_a_consignment_that_does_not_exist_is_rejected()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => service.RecordAsync("MCC-20260823-XX-99", SoundPanel()));
    }

    [Fact]
    public async Task Only_the_stages_the_cascade_ran_are_stored_in_order()
    {
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        var command = new RecordTestCommand(4.1m, 28.5m, 29.0m, 0m, KqColour.Blue,
            new Dictionary<AlcoholStage, StageOutcome>
            {
                [AlcoholStage.Alcohol80] = StageOutcome.Positive,
                [AlcoholStage.Alcohol75] = StageOutcome.Negative,
                [AlcoholStage.Alcohol68] = StageOutcome.Positive
            },
            TestVerdict.Accept, null, null, "officer-1");

        var view = await service.RecordAsync(reference, command);

        // The cascade halted at 75%, so the 68% reading was never part of it.
        Assert.Equal(["Alcohol80", "Alcohol75"], view.AlcoholStages.Select(stage => stage.Stage));
        Assert.Equal("Alcohol75", view.PassedAlcoholAt);
        Assert.Equal(nameof(StabilityGrade.MarginallyStable), view.StabilityGrade);
    }

    [Fact]
    public async Task A_recorded_test_can_be_read_back_with_everything_it_stored()
    {
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        await service.RecordAsync(reference, SoundPanel());

        await using var reader = _database.CreateContext();
        var view = await new QualityTestService(
            reader,
            new QualityPanelEvaluator(Options.Create(new QualityThresholds())),
            _clock).GetForConsignmentAsync(reference);

        Assert.NotNull(view);
        Assert.Equal(reference, view.ConsignmentReference);
        Assert.Equal(4.1m, view.FatPercent);
        Assert.Equal(28.5m, view.RawLactometerReading);
        Assert.Equal(nameof(KqColour.Blue), view.KqColour);
        Assert.Equal(_clock.UtcNow.UtcDateTime, view.TestedAtUtc);
        Assert.NotEmpty(view.AlcoholStages);
    }

    [Fact]
    public async Task Reading_back_a_consignment_that_was_never_tested_gives_nothing()
    {
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        await using var _ = context;

        Assert.Null(await service.GetForConsignmentAsync(reference));
    }

    [Fact]
    public void The_service_rejects_null_commands()
    {
        var service = CreateService(out var context);
        using var _ = context;

        Assert.Throws<ArgumentNullException>(() => service.Preview(null!));
    }

    [Fact]
    public void A_sound_sample_reports_every_sense_as_ok()
    {
        var service = CreateService(out var context);
        using var _ = context;

        var preview = service.Preview(SoundPanel());

        foreach (var sense in new[] { "Smell", "Colour", "Taste" })
        {
            var measure = preview.Measures.Single(m => m.Measure == sense);

            Assert.Equal("OK", measure.Value);
            Assert.False(measure.IsOutsideThreshold);
        }

        Assert.True(preview.MeetsStandard);
    }

    [Theory]
    [InlineData(false, true, true, "Smell")]
    [InlineData(true, false, true, "Colour")]
    [InlineData(true, true, false, "Taste")]
    public void A_sense_the_officer_found_wrong_fails_the_panel(
        bool smell,
        bool colour,
        bool taste,
        string expected)
    {
        var service = CreateService(out var context);
        using var _ = context;

        // Every instrument reading is sound here: sour milk is a reason to turn a delivery away
        // whatever the lactometer reads.
        var preview = service.Preview(SoundPanel(sensory: new SensoryCheck(smell, colour, taste)));

        var failed = preview.Measures.Single(measure => measure.IsOutsideThreshold);

        Assert.Equal(expected, failed.Measure);
        Assert.Equal("Not OK", failed.Value);
        Assert.Contains(expected.ToLowerInvariant(), failed.Detail);
        Assert.False(preview.MeetsStandard);
    }

    [Fact]
    public async Task The_senses_are_recorded_as_the_officer_found_them()
    {
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        using var _ = context;

        var view = await service.RecordAsync(
            reference,
            SoundPanel(
                verdict: TestVerdict.Reject,
                failedParameter: "Smell",
                failedValue: "Not OK",
                sensory: new SensoryCheck(SmellOk: false, ColourOk: true, TasteOk: true)));

        Assert.False(view.SmellOk);
        Assert.True(view.ColourOk);
        Assert.True(view.TasteOk);
    }

    [Fact]
    public async Task A_panel_that_says_nothing_about_the_senses_records_them_as_sound()
    {
        // A client written before the sensory check existed did not observe a fault, and reading
        // its panels back as faulty would restate history.
        var reference = await RegisterConsignmentAsync();
        var service = CreateService(out var context);
        using var _ = context;

        var view = await service.RecordAsync(reference, SoundPanel());

        Assert.True(view.SmellOk);
        Assert.True(view.ColourOk);
        Assert.True(view.TasteOk);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
