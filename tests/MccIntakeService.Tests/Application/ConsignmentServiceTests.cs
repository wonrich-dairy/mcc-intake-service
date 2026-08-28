using MccIntakeService.Application.Consignments;
using MccIntakeService.Configuration;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Infrastructure.Persistence;
using MccIntakeService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MccIntakeService.Tests.Application;

public class ConsignmentServiceTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    private readonly FakeIntakeClock _clock = new(new DateTime(2026, 8, 23, 8, 0, 0));

    private (MccIntakeDbContext Context, ConsignmentService Service) CreateService()
    {
        var context = _database.CreateContext();

        var service = new ConsignmentService(
            context,
            new ConsignmentReferenceGenerator(context),
            _clock,
            Options.Create(new IntakeOptions()),
            NullLogger<ConsignmentService>.Instance);

        return (context, service);
    }

    private static RegisterConsignmentCommand CommandFor(
        Guid societyId,
        DateTime? arrival = null,
        params CanEntry[] cans) =>
        new(societyId, cans.Length == 0 ? [new CanEntry(1, 40.5m), new CanEntry(2, 39.5m)] : cans, arrival);

    [Fact]
    public async Task Registering_stores_the_consignment_with_its_cans_and_derived_total()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        var society = _database.Society("KC");

        var view = await service.RegisterAsync(CommandFor(society.Id));

        Assert.Equal("MCC-20260823-KC-01", view.Reference);
        Assert.Equal(80m, view.TotalQuantityKg);
        Assert.Equal(2, view.CanCount);
        Assert.Equal(["KC 01", "KC 02"], view.Cans.Select(can => can.CanLabel));
        Assert.Equal("KC", view.SocietyCode);
        Assert.Equal(ConsignmentStatus.Registered, view.Status);

        await using var verification = _database.CreateContext();
        var stored = await verification.Consignments
            .Include(consignment => consignment.Society)
            .SingleAsync();

        Assert.Equal("MCC-20260823-KC-01", stored.Reference);
        Assert.Equal(80m, stored.TotalQuantityKg);
        Assert.Equal(2, stored.Cans.Count);
    }

    [Fact]
    public async Task Registering_captures_the_arrival_time_automatically_when_none_is_supplied()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        var view = await service.RegisterAsync(CommandFor(_database.Society("KC").Id));

        Assert.Equal(_clock.LocalNow, view.ArrivalAtLocal);
        Assert.Equal(new DateOnly(2026, 8, 23), view.ArrivalDate);
    }

    [Fact]
    public async Task Registering_honours_an_arrival_time_the_officer_corrected_before_submitting()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        var corrected = new DateTime(2026, 8, 23, 6, 15, 0);

        var view = await service.RegisterAsync(CommandFor(_database.Society("KC").Id, corrected));

        Assert.Equal(corrected, view.ArrivalAtLocal);
    }

    [Fact]
    public async Task Registering_against_a_society_that_does_not_exist_is_rejected()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => service.RegisterAsync(CommandFor(Guid.NewGuid())));

        Assert.Equal("Society", exception.Entity);
    }

    [Fact]
    public async Task Registering_after_the_daily_cutoff_is_blocked_and_nothing_is_stored()
    {
        _clock.LocalNow = new DateTime(2026, 8, 23, 16, 30, 0);

        var (context, service) = CreateService();
        await using var _ = context;

        await Assert.ThrowsAsync<IntakeCutoffExceededException>(
            () => service.RegisterAsync(CommandFor(_database.Society("KC").Id)));

        await using var verification = _database.CreateContext();
        Assert.Empty(verification.Consignments);
    }

    [Fact]
    public async Task Consecutive_registrations_for_one_society_receive_incrementing_references()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        var societyId = _database.Society("KC").Id;

        var first = await service.RegisterAsync(CommandFor(societyId));
        var second = await service.RegisterAsync(CommandFor(societyId));

        Assert.Equal("MCC-20260823-KC-01", first.Reference);
        Assert.Equal("MCC-20260823-KC-02", second.Reference);
    }

    [Fact]
    public async Task A_consignment_can_be_fetched_by_its_reference_regardless_of_casing()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        var registered = await service.RegisterAsync(CommandFor(_database.Society("KC").Id));

        var found = await service.GetByReferenceAsync("mcc-20260823-kc-01");

        Assert.NotNull(found);
        Assert.Equal(registered.Id, found.Id);
        Assert.Equal("Kandy Co-operative Dairy Society", found.SocietyName);
        Assert.Equal(2, found.Cans.Count);
    }

    [Theory]
    [InlineData("MCC-20260823-KC-99")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Fetching_an_unknown_or_blank_reference_yields_nothing(string reference)
    {
        var (context, service) = CreateService();
        await using var _ = context;

        Assert.Null(await service.GetByReferenceAsync(reference));
    }

    [Fact]
    public async Task Consignments_can_be_searched_by_society()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        await service.RegisterAsync(CommandFor(_database.Society("KC").Id));
        await service.RegisterAsync(CommandFor(_database.Society("MT").Id));

        var byId = await service.SearchAsync(new ConsignmentQuery { SocietyId = _database.Society("KC").Id });
        var byCode = await service.SearchAsync(new ConsignmentQuery { SocietyCode = "mt" });

        Assert.Equal("MCC-20260823-KC-01", Assert.Single(byId.Items).Reference);
        Assert.Equal("MCC-20260823-MT-01", Assert.Single(byCode.Items).Reference);
    }

    [Fact]
    public async Task Consignments_can_be_searched_by_date_and_by_date_range()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        var societyId = _database.Society("KC").Id;

        _clock.LocalNow = new DateTime(2026, 8, 21, 8, 0, 0);
        await service.RegisterAsync(CommandFor(societyId));

        _clock.LocalNow = new DateTime(2026, 8, 23, 8, 0, 0);
        await service.RegisterAsync(CommandFor(societyId));

        var onOneDay = await service.SearchAsync(new ConsignmentQuery { ArrivalDate = new DateOnly(2026, 8, 21) });
        var acrossRange = await service.SearchAsync(new ConsignmentQuery
        {
            FromDate = new DateOnly(2026, 8, 22),
            ToDate = new DateOnly(2026, 8, 24)
        });

        Assert.Equal("MCC-20260821-KC-01", Assert.Single(onOneDay.Items).Reference);
        Assert.Equal("MCC-20260823-KC-01", Assert.Single(acrossRange.Items).Reference);
    }

    [Fact]
    public async Task Consignments_can_be_searched_by_reference()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        var societyId = _database.Society("KC").Id;
        await service.RegisterAsync(CommandFor(societyId));
        await service.RegisterAsync(CommandFor(societyId));

        var result = await service.SearchAsync(new ConsignmentQuery { Reference = "MCC-20260823-KC-02" });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("MCC-20260823-KC-02", Assert.Single(result.Items).Reference);
    }

    [Fact]
    public async Task Search_results_are_paged_and_report_the_total()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        var societyId = _database.Society("KC").Id;
        for (var i = 0; i < 5; i++)
        {
            await service.RegisterAsync(CommandFor(societyId));
        }

        var page = await service.SearchAsync(new ConsignmentQuery { Page = 2, PageSize = 2 });

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task Search_clamps_out_of_range_paging_values()
    {
        var (context, service) = CreateService();
        await using var _ = context;

        await service.RegisterAsync(CommandFor(_database.Society("KC").Id));

        var page = await service.SearchAsync(new ConsignmentQuery { Page = 0, PageSize = 0 });

        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Single(page.Items);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
