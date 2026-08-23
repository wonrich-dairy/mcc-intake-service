using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Societies;

namespace MccIntakeService.Tests.Domain;

/// <summary>Covers the society lifecycle rules introduced by SCRUM-51.</summary>
public class SocietyManagementTests
{
    private static Society Kandy() => new(
        Guid.NewGuid(),
        "KC",
        "Kandy Co-operative Dairy Society",
        "KC",
        contactPerson: "Sunil Perera",
        contactNumber: "+94 81 222 3344");

    [Fact]
    public void A_society_records_its_contact_person_and_number()
    {
        var society = Kandy();

        Assert.Equal("Sunil Perera", society.ContactPerson);
        Assert.Equal("+94 81 222 3344", society.ContactNumber);
    }

    [Fact]
    public void Contact_details_are_optional()
    {
        var society = new Society(Guid.NewGuid(), "TH", "Thalawakele Society", "TH");

        Assert.Null(society.ContactPerson);
        Assert.Null(society.ContactNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_contact_details_are_stored_as_absent_rather_than_empty(string blank)
    {
        var society = new Society(Guid.NewGuid(), "TH", "Thalawakele Society", "TH", blank, blank);

        Assert.Null(society.ContactPerson);
        Assert.Null(society.ContactNumber);
    }

    [Fact]
    public void Details_can_be_amended_without_touching_the_code()
    {
        var society = Kandy();

        society.UpdateDetails("Kandy Dairy Society", "KD", "Nimal Silva", "+94 81 999 0000");

        Assert.Equal("KC", society.Code);
        Assert.Equal("Kandy Dairy Society", society.Name);
        Assert.Equal("KD", society.CanLabelPrefix);
        Assert.Equal("Nimal Silva", society.ContactPerson);
    }

    [Fact]
    public void Contact_details_can_be_cleared_by_amending_them_to_nothing()
    {
        var society = Kandy();

        society.UpdateDetails(society.Name, society.CanLabelPrefix, null, null);

        Assert.Null(society.ContactPerson);
        Assert.Null(society.ContactNumber);
    }

    [Fact]
    public void The_code_can_be_changed_while_no_consignments_exist()
    {
        var society = Kandy();

        society.ChangeCode("kd", hasConsignments: false);

        Assert.Equal("KD", society.Code);
    }

    [Fact]
    public void The_code_is_frozen_once_consignments_have_been_registered()
    {
        var society = Kandy();

        var exception = Assert.Throws<DomainValidationException>(
            () => society.ChangeCode("KD", hasConsignments: true));

        Assert.Contains("cannot be changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("KC", society.Code);
    }

    [Fact]
    public void Re_submitting_the_same_code_is_allowed_even_when_consignments_exist()
    {
        // An edit that leaves the code alone must not trip the freeze rule.
        var society = Kandy();

        society.ChangeCode("kc", hasConsignments: true);

        Assert.Equal("KC", society.Code);
    }

    [Fact]
    public void A_society_can_be_deactivated_and_returned_to_service()
    {
        var society = Kandy();

        society.Deactivate();
        Assert.False(society.IsActive);

        society.Reactivate();
        Assert.True(society.IsActive);
    }

    [Fact]
    public void An_over_long_name_is_rejected()
    {
        var tooLong = new string('x', Society.MaxNameLength + 1);

        Assert.Throws<DomainValidationException>(
            () => new Society(Guid.NewGuid(), "KC", tooLong, "KC"));
    }

    [Fact]
    public void An_over_long_code_is_rejected()
    {
        var tooLong = new string('x', Society.MaxCodeLength + 1);

        Assert.Throws<DomainValidationException>(
            () => new Society(Guid.NewGuid(), tooLong, "Kandy Society", "KC"));
    }

    [Fact]
    public void An_over_long_contact_person_is_rejected()
    {
        var tooLong = new string('x', Society.MaxContactPersonLength + 1);

        var exception = Assert.Throws<DomainValidationException>(
            () => new Society(Guid.NewGuid(), "KC", "Kandy Society", "KC", tooLong));

        Assert.Contains("ContactPerson", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_over_long_contact_number_is_rejected()
    {
        var tooLong = new string('9', Society.MaxContactNumberLength + 1);

        Assert.Throws<DomainValidationException>(
            () => new Society(Guid.NewGuid(), "KC", "Kandy Society", "KC", "Sunil", tooLong));
    }

    [Fact]
    public void A_duplicate_code_reports_the_code_that_collided()
    {
        var exception = new DuplicateCodeException("Society", "KC");

        Assert.Equal("duplicate_code", exception.Code);
        Assert.Equal("KC", exception.ConflictingCode);
        Assert.Contains("KC", exception.Message, StringComparison.Ordinal);
    }
}
