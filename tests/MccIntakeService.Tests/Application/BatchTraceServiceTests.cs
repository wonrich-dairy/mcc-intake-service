using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Dispatch;
using MccIntakeService.Application.Factory;
using MccIntakeService.Application.QualityTests;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Application.Traceability;
using MccIntakeService.Configuration;
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

/// <summary>Covers resolving a batch back to its tanks and consignments (SCRUM-12).</summary>
public class BatchTraceServiceTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    private readonly FakeIntakeClock _clock = new(new DateTime(2026, 8, 23, 16, 0, 0));

    private static readonly QualityThresholds Thresholds = new();

    private BatchTraceService CreateService(out MccIntakeDbContext context)
    {
        context = _database.CreateContext();

        return new BatchTraceService(context, Options.Create(Thresholds));
    }

    /// <summary>Registers a consignment, tests it at the gate and pours it into a tank.</summary>
    private async Task<string> PourAsync(
        string tankCode,
        string societyCode,
        decimal fat = 4.5m,
        decimal raw = 29.5m,
        bool test = true)
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
            reference = (await service.RegisterAsync(new RegisterConsignmentCommand(
                society.Id, [new CanEntry(1, 515m)], null, "gate-officer"))).Reference;
        }

        if (!test)
        {
            return reference;
        }

        await using (var testing = _database.CreateContext())
        {
            await new QualityTestService(
                testing,
                new QualityPanelEvaluator(Options.Create(Thresholds)),
                _clock).RecordAsync(reference, new RecordTestCommand(
                    fat, raw, 29.0m, 0m, KqColour.Blue,
                    new Dictionary<AlcoholStage, StageOutcome> { [AlcoholStage.Alcohol80] = StageOutcome.Negative },
                    TestVerdict.Accept, TestedBy: "gate-officer"));
        }

        await using var pouring = _database.CreateContext();
        await new TankService(pouring, _clock).PourAsync(tankCode, reference, "pour-officer");

        return reference;
    }

    /// <summary>Dispatches the given tanks and screens the arrival, returning the batch reference.</summary>
    private async Task<string> BatchAsync(params string[] tankCodes)
    {
        string note;

        await using (var dispatching = _database.CreateContext())
        {
            note = (await new DispatchService(dispatching, _clock).RecordAsync(new RecordDispatchCommand(
                "WP-CAB-1234", "Ranjith Fernando",
                tankCodes.Select(code => new DispatchDrawCommand(code, 100m)).ToList(),
                4.0m, 8.6m, KqColour.Blue, StabilityGrade.Stable, 4.5m, null, null, "manager-1"))).Reference;
        }

        await using var factory = _database.CreateContext();
        var screening = await new FactoryIntakeService(factory, _clock).ScreenAsync(
            new ScreenArrivalCommand(note, true, true, true, 4.8m, null, "factory-officer"));

        return screening.Batch!.Reference;
    }

    [Fact]
    public async Task A_batch_resolves_to_its_dispatch_note_tanks_and_consignments()
    {
        var consignment = await PourAsync("T1", "KC");
        var batch = await BatchAsync("T1");

        var service = CreateService(out var context);
        await using var _ = context;

        var trace = await service.TraceAsync(batch);

        Assert.NotNull(trace);
        Assert.Equal(batch, trace.BatchReference);
        Assert.StartsWith("DN-", trace.DispatchNoteReference);
        Assert.Equal("WP-CAB-1234", trace.BowserRegistration);

        var tank = Assert.Single(trace.Tanks);
        Assert.Equal("T1", tank.TankCode);
        Assert.Equal(100m, tank.QuantityDrawnLitres);
        Assert.Equal(consignment, Assert.Single(tank.Consignments).Reference);
    }

    [Fact]
    public async Task A_batch_traces_only_the_load_its_bowser_carried()
    {
        var carried = await PourAsync("T1", "KC");

        // Draw the tank dry so the dispatch closes its fill, then start the next load in the
        // same tank. Resolving through the whole tank rather than the fill would put milk the
        // bowser never carried on the batch — and score its society's standing on it.
        decimal held;

        await using (var reading = _database.CreateContext())
        {
            held = (await new TankService(reading, _clock).ListAsync())
                .Single(tank => tank.Code == "T1").AvailableQuantityLitres;
        }

        string note;

        await using (var dispatching = _database.CreateContext())
        {
            note = (await new DispatchService(dispatching, _clock).RecordAsync(new RecordDispatchCommand(
                "WP-CAB-1234", "Ranjith Fernando",
                [new DispatchDrawCommand("T1", held)],
                4.0m, 8.6m, KqColour.Blue, StabilityGrade.Stable, 4.5m, null, null, "manager-1"))).Reference;
        }

        string batch;

        await using (var factory = _database.CreateContext())
        {
            batch = (await new FactoryIntakeService(factory, _clock).ScreenAsync(
                new ScreenArrivalCommand(note, true, true, true, 4.8m, null, "factory-officer")))
                .Batch!.Reference;
        }

        await PourAsync("T1", "MT");

        var service = CreateService(out var context);
        await using var _ = context;

        var trace = await service.TraceAsync(batch);

        var tank = Assert.Single(trace!.Tanks);
        Assert.Equal(carried, Assert.Single(tank.Consignments).Reference);
        Assert.Equal("KC", Assert.Single(trace.SocietiesByMargin).SocietyCode);
    }

    [Fact]
    public async Task A_batch_drawing_from_more_than_one_tank_resolves_every_tank()
    {
        await PourAsync("T1", "KC");
        await PourAsync("T2", "MT");
        await PourAsync("T3", "NW");
        var batch = await BatchAsync("T1", "T2", "T3");

        var service = CreateService(out var context);
        await using var _ = context;

        var trace = await service.TraceAsync(batch);

        Assert.Equal(["T1", "T2", "T3"], trace!.Tanks.Select(tank => tank.TankCode));
        Assert.All(trace.Tanks, tank => Assert.Single(tank.Consignments));
        Assert.Equal(300m, trace.TotalDispatchedLitres);
    }

    [Fact]
    public async Task Each_consignment_carries_its_society_cans_quantity_and_gate_results()
    {
        await PourAsync("T1", "KC");
        var batch = await BatchAsync("T1");

        var service = CreateService(out var context);
        await using var _ = context;

        var consignment = Assert.Single(Assert.Single((await service.TraceAsync(batch))!.Tanks).Consignments);

        Assert.Equal("KC", consignment.SocietyCode);
        Assert.Equal(["KC 01"], consignment.CanLabels);
        Assert.True(consignment.QuantityLitres > 0m);
        Assert.NotNull(consignment.QualityTest);
        Assert.Equal(4.5m, consignment.QualityTest.FatPercent);
        Assert.Equal("Accept", consignment.QualityTest.Verdict);
        Assert.NotEmpty(consignment.QualityTest.Margins);
    }

    [Fact]
    public async Task Every_record_carries_its_timestamp_and_recording_officer()
    {
        await PourAsync("T1", "KC");
        var batch = await BatchAsync("T1");

        var service = CreateService(out var context);
        await using var _ = context;

        var trace = await service.TraceAsync(batch);
        var consignment = Assert.Single(Assert.Single(trace!.Tanks).Consignments);

        Assert.Equal("factory-officer", trace.ScreenedBy);
        Assert.Equal("manager-1", trace.DispatchedBy);
        Assert.Equal("gate-officer", consignment.RegisteredBy);
        Assert.Equal("pour-officer", consignment.PouredBy);
        Assert.Equal("gate-officer", consignment.QualityTest!.TestedBy);
        Assert.NotEqual(default, trace.CreatedAtUtc);
        Assert.NotEqual(default, consignment.PouredAtUtc);
    }

    [Fact]
    public async Task Societies_are_ranked_with_the_most_marginal_first()
    {
        // KC sits close to the SNF and CLR floors; NW has ample room on both.
        await PourAsync("T1", "KC", fat: 3.6m, raw: 26.4m);
        await PourAsync("T1", "NW", fat: 6.0m, raw: 31.0m);
        var batch = await BatchAsync("T1");

        var service = CreateService(out var context);
        await using var _ = context;

        var ranked = (await service.TraceAsync(batch))!.SocietiesByMargin;

        Assert.Equal("KC", ranked[0].SocietyCode);
        Assert.Equal("NW", ranked[^1].SocietyCode);
        Assert.True(ranked[0].TightestMargin < ranked[^1].TightestMargin);
        Assert.False(string.IsNullOrEmpty(ranked[0].TightestMeasure));
    }

    [Fact]
    public async Task The_margins_name_the_measure_that_sat_closest_to_its_limit()
    {
        await PourAsync("T1", "KC", fat: 3.55m, raw: 31.0m);
        var batch = await BatchAsync("T1");

        var service = CreateService(out var context);
        await using var _ = context;

        var consignment = Assert.Single(Assert.Single((await service.TraceAsync(batch))!.Tanks).Consignments);

        // Fat at 3.55 against a 3.50 floor is by far the tightest of the measures.
        Assert.Equal("FatPercent", consignment.QualityTest!.Margins[0].Measure);
        Assert.Equal(consignment.QualityTest.Margins[0].Margin, consignment.TightestMargin);
    }

    [Fact]
    public async Task A_consignment_with_no_gate_test_is_reported_as_missing_not_omitted()
    {
        // Pour one tested consignment, then plant an untested one in the same tank.
        await PourAsync("T1", "KC");

        var untested = await PourAsync("T1", "MT", test: false);

        await using (var context = _database.CreateContext())
        {
            // Accept it directly so it is pourable without a recorded panel — the shape of a
            // consignment whose gate results went missing upstream.
            var consignment = await context.Consignments.SingleAsync(c => c.Reference == untested);
            consignment.SettleGateVerdict(TestVerdict.Accept);
            await context.SaveChangesAsync();
        }

        await using (var pouring = _database.CreateContext())
        {
            await new TankService(pouring, _clock).PourAsync("T1", untested, "pour-officer");
        }

        var batch = await BatchAsync("T1");
        var service = CreateService(out var context2);
        await using var _ = context2;

        var trace = await service.TraceAsync(batch);
        var gap = Assert.Single(
            Assert.Single(trace!.Tanks).Consignments,
            c => c.Reference == untested);

        Assert.Null(gap.QualityTest);
        Assert.Contains(gap.Missing, entry => entry.Contains("quality test", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(MarginToThreshold.Unknown, gap.TightestMargin);
    }

    [Fact]
    public async Task A_society_with_no_gate_results_ranks_last_rather_than_first()
    {
        await PourAsync("T1", "KC", fat: 3.6m, raw: 26.4m);

        var untested = await PourAsync("T1", "MT", test: false);

        await using (var context = _database.CreateContext())
        {
            var consignment = await context.Consignments.SingleAsync(c => c.Reference == untested);
            consignment.SettleGateVerdict(TestVerdict.Accept);
            await context.SaveChangesAsync();
        }

        await using (var pouring = _database.CreateContext())
        {
            await new TankService(pouring, _clock).PourAsync("T1", untested, "pour-officer");
        }

        var batch = await BatchAsync("T1");
        var service = CreateService(out var context2);
        await using var _ = context2;

        var ranked = (await service.TraceAsync(batch))!.SocietiesByMargin;

        // Unknown is not the same as safe, but it must not masquerade as the most marginal either.
        Assert.Equal("KC", ranked[0].SocietyCode);
        Assert.Equal("MT", ranked[^1].SocietyCode);
        Assert.Equal(MarginToThreshold.Unknown, ranked[^1].TightestMargin);
    }

    [Fact]
    public async Task A_tank_with_no_pours_is_reported_as_missing()
    {
        await PourAsync("T1", "KC");
        // T2 is dispatched from without anything having been poured into it.
        await using (var context = _database.CreateContext())
        {
            var tank = await context.ChillingTanks.SingleAsync(t => t.Code == "T2");
            Assert.Equal(1, tank.FillNumber);
        }

        var service = CreateService(out var context2);
        await using var _ = context2;

        var trace = await service.TraceAsync(await BatchAsync("T1"));

        Assert.Empty(Assert.Single(trace!.Tanks).Missing);
    }

    [Fact]
    public void Added_water_is_scored_against_its_ceiling()
    {
        // Every other measure fails by being too low; this one fails by being too high, so its
        // room is the distance down from the ceiling. 0.49 against a 0.50 limit leaves 2%.
        var margins = MarginToThreshold.For(FakeTest(4.5m, water: 0.49m), Thresholds);

        var water = Assert.Single(margins, margin => margin.Measure == "WaterPercent");

        Assert.Equal(0.02m, water.Margin);
        Assert.Equal("0.50", water.Threshold);
    }

    [Fact]
    public void A_consignment_close_to_the_water_limit_scores_on_it()
    {
        // Comfortable on every other measure, 2% of room against the adulteration limit. While
        // added water went unscored this consignment ranked as though it were clean, which is
        // the wrong supplier to point a QCO chasing adulteration at.
        var tightest = MarginToThreshold.Tightest(FakeTest(4.5m, water: 0.49m), Thresholds);

        Assert.Equal(0.02m, tightest);
        Assert.True(tightest < MarginToThreshold.Tightest(FakeTest(4.5m), Thresholds));
    }

    [Fact]
    public async Task An_unknown_batch_reference_gives_nothing()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        Assert.Null(await service.TraceAsync("WR-20260823-99"));
    }

    [Fact]
    public void The_margin_calculation_is_tighter_the_closer_a_value_sits_to_its_floor()
    {
        var thresholds = new QualityThresholds { MinimumFatPercent = 3.5m };

        // 3.55 against a 3.50 floor leaves about 1.4%; 7.00 leaves 100%.
        Assert.True(
            MarginToThreshold.For(FakeTest(3.55m), thresholds).First(m => m.Measure == "FatPercent").Margin <
            MarginToThreshold.For(FakeTest(7.00m), thresholds).First(m => m.Measure == "FatPercent").Margin);
    }

    private static QualityTest FakeTest(decimal fat, decimal water = 0m)
    {
        var society = new MccIntakeService.Domain.Societies.Society(Guid.NewGuid(), "KC", "Kandy", "KC");

        var consignment = Consignment.Register(
            Guid.NewGuid(), "MCC-20260823-KC-01", society,
            new DateTime(2026, 8, 23, 7, 0, 0), [new CanEntry(1, 41.2m)], 1.03m,
            new TimeOnly(16, 0), new DateTime(2026, 8, 23, 8, 0, 0), DateTimeOffset.UtcNow);

        var readings = new PanelReadings(fat, 29.5m, 29.0m, water,
            new Dictionary<AlcoholStage, StageOutcome> { [AlcoholStage.Alcohol80] = StageOutcome.Negative },
            KqColour.Blue);

        var result = new QualityPanelEvaluator(Options.Create(Thresholds)).Evaluate(readings);

        return QualityTest.Record(
            Guid.NewGuid(), consignment, readings, SensoryCheck.Sound, result,
            TestVerdict.Accept, null, null, "officer", DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
