using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Infrastructure.Persistence;
using MccIntakeService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MccIntakeService.Tests.Application;

/// <summary>Covers the society CRUD behaviour delivered by SCRUM-51.</summary>
public class SocietyManagementServiceTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    private SocietyService CreateService(out MccIntakeDbContext context)
    {
        context = _database.CreateContext();

        return new SocietyService(context);
    }

    private static CreateSocietyCommand NewSociety(string code = "TH") =>
        new(code, "Thalawakele Tea Country Society", code, "Sunil Perera", "+94 51 222 1111");

    [Fact]
    public async Task A_society_can_be_registered_with_its_contact_details()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var society = await service.CreateAsync(NewSociety());

        Assert.Equal("TH", society.Code);
        Assert.Equal("Sunil Perera", society.ContactPerson);
        Assert.True(society.IsActive);

        await using var verification = _database.CreateContext();
        Assert.True(await verification.Societies.AnyAsync(s => s.Code == "TH"));
    }

    [Fact]
    public async Task A_society_code_is_normalised_to_upper_case_on_creation()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var society = await service.CreateAsync(NewSociety("th"));

        Assert.Equal("TH", society.Code);
    }

    [Fact]
    public async Task A_duplicate_code_is_rejected()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<DuplicateCodeException>(
            () => service.CreateAsync(NewSociety("KC")));

        Assert.Equal("KC", exception.ConflictingCode);
    }

    [Fact]
    public async Task A_duplicate_code_is_rejected_regardless_of_the_casing_submitted()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<DuplicateCodeException>(() => service.CreateAsync(NewSociety("kc")));
    }

    [Fact]
    public async Task A_society_can_be_amended()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewSociety());

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateSocietyCommand("TH", "Thalawakele Highland Society", "TL", "Nimal Silva", "+94 51 222 9999"));

        Assert.Equal("Thalawakele Highland Society", updated.Name);
        Assert.Equal("TL", updated.CanLabelPrefix);
        Assert.Equal("Nimal Silva", updated.ContactPerson);
    }

    [Fact]
    public async Task The_code_can_be_moved_while_no_consignments_exist()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewSociety());

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateSocietyCommand("TL", created.Name, created.CanLabelPrefix, null, null));

        Assert.Equal("TL", updated.Code);
    }

    [Fact]
    public async Task The_code_is_frozen_once_a_consignment_has_been_registered()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var kandy = _database.Society("KC");
        await RegisterConsignmentAsync(kandy.Id);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => service.UpdateAsync(
                kandy.Id,
                new UpdateSocietyCommand("KD", kandy.Name, kandy.CanLabelPrefix, null, null)));

        Assert.Contains("cannot be changed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_society_with_consignments_can_still_have_its_other_details_amended()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var kandy = _database.Society("KC");
        await RegisterConsignmentAsync(kandy.Id);

        var updated = await service.UpdateAsync(
            kandy.Id,
            new UpdateSocietyCommand(kandy.Code, "Kandy Dairy Co-op", "KC", "Nimal Silva", "+94 81 111 2222"));

        Assert.Equal("Kandy Dairy Co-op", updated.Name);
        Assert.Equal("KC", updated.Code);
    }

    [Fact]
    public async Task Amending_a_society_onto_a_code_another_society_holds_is_rejected()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewSociety());

        await Assert.ThrowsAsync<DuplicateCodeException>(
            () => service.UpdateAsync(
                created.Id,
                new UpdateSocietyCommand("MT", created.Name, created.CanLabelPrefix, null, null)));
    }

    [Fact]
    public async Task Amending_a_society_while_keeping_its_own_code_is_not_a_duplicate()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewSociety());

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateSocietyCommand("TH", "Renamed Society", "TH", null, null));

        Assert.Equal("Renamed Society", updated.Name);
    }

    [Fact]
    public async Task Amending_a_society_that_does_not_exist_is_rejected()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => service.UpdateAsync(Guid.NewGuid(), new UpdateSocietyCommand("XX", "Nowhere", "XX", null, null)));
    }

    [Fact]
    public async Task A_deactivated_society_disappears_from_the_default_list_but_is_still_retrievable()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var kandy = _database.Society("KC");
        await service.DeactivateAsync(kandy.Id);

        var active = await service.ListAsync();
        var all = await service.ListAsync(new SocietyQuery { ActiveOnly = false });

        Assert.DoesNotContain(active, society => society.Code == "KC");
        Assert.Contains(all, society => society.Code == "KC");
        Assert.False((await service.GetAsync(kandy.Id))!.IsActive);
    }

    [Fact]
    public async Task A_deactivated_society_cannot_be_selected_for_a_new_consignment()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var kandy = _database.Society("KC");
        await service.DeactivateAsync(kandy.Id);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => RegisterConsignmentAsync(kandy.Id));

        Assert.Contains("no longer active", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_retired_society_can_be_returned_to_service()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var kandy = _database.Society("KC");
        await service.DeactivateAsync(kandy.Id);

        var reactivated = await service.ReactivateAsync(kandy.Id);

        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task Deactivating_a_society_that_does_not_exist_is_rejected()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.DeactivateAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.ReactivateAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("kandy", "KC")]
    [InlineData("MT", "MT")]
    [InlineData("Highland", "NW")]
    public async Task The_list_is_searchable_by_name_and_by_code(string term, string expectedCode)
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var results = await service.ListAsync(new SocietyQuery { Search = term });

        Assert.Equal(expectedCode, Assert.Single(results).Code);
    }

    [Fact]
    public async Task A_search_matching_nothing_returns_an_empty_list()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        Assert.Empty(await service.ListAsync(new SocietyQuery { Search = "zzz-nothing" }));
    }

    [Fact]
    public async Task The_list_can_be_sorted_by_code_or_name_in_either_direction()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var byCode = await service.ListAsync(new SocietyQuery { SortBy = SocietySortBy.Code });
        var byCodeDesc = await service.ListAsync(new SocietyQuery { SortBy = SocietySortBy.Code, Descending = true });
        var byName = await service.ListAsync(new SocietyQuery { SortBy = SocietySortBy.Name });

        Assert.Equal(["BD", "KC", "MT", "NW"], byCode.Select(society => society.Code));
        Assert.Equal(["NW", "MT", "KC", "BD"], byCodeDesc.Select(society => society.Code));
        Assert.Equal("Badulla Uva Milk Society", byName.First().Name);
    }

    private async Task RegisterConsignmentAsync(Guid societyId)
    {
        await using var context = _database.CreateContext();

        var service = new ConsignmentService(
            context,
            new ConsignmentReferenceGenerator(context),
            new FakeIntakeClock(new DateTime(2026, 8, 23, 8, 0, 0)),
            NullLogger<ConsignmentService>.Instance);

        await service.RegisterAsync(new RegisterConsignmentCommand(societyId, [new CanEntry(1, 40m)]));
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
