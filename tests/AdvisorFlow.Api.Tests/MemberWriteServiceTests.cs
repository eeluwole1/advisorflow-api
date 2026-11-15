using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;
using AdvisorFlow.Infrastructure.Crm;

namespace AdvisorFlow.Api.Tests;

/// <summary>Member create/update rules (M3): validation and the primary-member rule BR-HH-003, with the mock provider.</summary>
public class MemberWriteServiceTests
{
    private const string Williams = "001MOCK00000000001";   // James (primary) and Sarah
    private const string Okafor = "001MOCK00000000002";     // Chinedu (primary) and Amara
    private const string Martin = "001MOCK00000000007";     // no members
    private const string James = "003MOCK00000000001";
    private const string Sarah = "003MOCK00000000002";

    private readonly HouseholdService _service = new(new MockCrmDataProvider());

    private static MemberDraft Draft(MemberWriteRequest request)
    {
        var errors = HouseholdService.ValidateMember(request, out var draft);
        Assert.Empty(errors);
        Assert.NotNull(draft);
        return draft;
    }

    private static string[] ErrorFields(MemberWriteRequest? request) =>
        HouseholdService.ValidateMember(request, out _).Keys.Order().ToArray();

    private async Task<string[]> PrimaryIds(string householdId) =>
        (await _service.GetMembersAsync(householdId, default))!.Where(m => m.IsPrimary).Select(m => m.Id).ToArray();

    [Fact]
    public void Values_are_trimmed_and_the_contact_method_uses_its_exact_spelling()
    {
        var draft = Draft(new MemberWriteRequest(" Ada ", " Okafor ", " ada@example.com ", "204-555-0170", "sms", null));

        Assert.Equal(new MemberDraft("Ada", "Okafor", "ada@example.com", "204-555-0170", "SMS", false), draft);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Last_name_is_required(string? lastName)
    {
        Assert.Equal(new[] { "lastName" }, ErrorFields(new MemberWriteRequest("Ada", lastName, null, null, null, null)));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("two@@example.com")]
    [InlineData("no spaces@example.com")]
    public void Email_must_be_a_valid_address(string email)
    {
        Assert.Equal(new[] { "email" }, ErrorFields(new MemberWriteRequest(null, "Okafor", email, null, null, null)));
    }

    [Fact]
    public void Too_long_values_are_rejected()
    {
        var request = new MemberWriteRequest(new string('a', 41), new string('b', 81), null, new string('1', 41), null, null);

        Assert.Equal(new[] { "firstName", "lastName", "phone" }, ErrorFields(request));
    }

    [Theory]
    [InlineData("Email", null, null)]
    [InlineData("Phone", "ada@example.com", null)]
    [InlineData("SMS", "ada@example.com", null)]
    public void The_preferred_method_needs_its_contact_detail(string method, string? email, string? phone)
    {
        Assert.Equal(new[] { "preferredContactMethod" }, ErrorFields(new MemberWriteRequest(null, "Okafor", email, phone, method, null)));
    }

    [Fact]
    public void An_unapproved_contact_method_is_rejected()
    {
        Assert.Equal(new[] { "preferredContactMethod" }, ErrorFields(new MemberWriteRequest(null, "Okafor", null, null, "Fax", null)));
    }

    [Fact]
    public void A_missing_body_is_rejected()
    {
        Assert.Equal(new[] { "body" }, ErrorFields(null));
    }

    [Fact]
    public async Task The_first_member_of_a_household_becomes_primary() // BR-HH-003
    {
        var result = await _service.CreateMemberAsync(Martin, Draft(new MemberWriteRequest("Luc", "Martin", null, null, null, null)), default);

        Assert.Equal(MemberWriteStatus.Ok, result.Status);
        Assert.NotNull(result.Member);
        Assert.True(result.Member.IsPrimary);
        Assert.Matches("^003MOCK[0-9]{11}$", result.Member.Id);
        Assert.Equal(new[] { result.Member.Id }, await PrimaryIds(Martin));
    }

    [Fact]
    public async Task A_later_member_is_not_primary_unless_asked()
    {
        var result = await _service.CreateMemberAsync(Williams, Draft(new MemberWriteRequest("Ava", "Williams", null, null, null, null)), default);

        Assert.False(result.Member!.IsPrimary);
        Assert.Equal(new[] { James }, await PrimaryIds(Williams));
    }

    [Fact]
    public async Task Adding_a_primary_member_replaces_the_previous_primary() // BR-HH-003
    {
        var result = await _service.CreateMemberAsync(Williams, Draft(new MemberWriteRequest("Ava", "Williams", null, null, null, true)), default);

        Assert.True(result.Member!.IsPrimary);
        Assert.Equal(new[] { result.Member.Id }, await PrimaryIds(Williams));
    }

    [Fact]
    public async Task Making_a_member_primary_clears_the_previous_primary() // BR-HH-003
    {
        var draft = Draft(new MemberWriteRequest("Sarah", "Williams", "sarah.williams@example.com", "204-555-0111", "Email", true));

        var result = await _service.UpdateMemberAsync(Williams, Sarah, draft, default);

        Assert.Equal(MemberWriteStatus.Ok, result.Status);
        Assert.Equal(new[] { Sarah }, await PrimaryIds(Williams));
        var members = await _service.GetMembersAsync(Williams, default);
        Assert.Equal(Sarah, members![0].Id); // primary first
    }

    [Fact]
    public async Task The_current_primary_cannot_be_unset() // BR-HH-003
    {
        var draft = Draft(new MemberWriteRequest("James", "Williams", null, null, null, false));

        var result = await _service.UpdateMemberAsync(Williams, James, draft, default);

        Assert.Equal(MemberWriteStatus.CannotUnsetPrimary, result.Status);
        Assert.Equal(new[] { James }, await PrimaryIds(Williams));
    }

    [Fact]
    public async Task Update_replaces_fields_and_clears_empty_ones()
    {
        var draft = Draft(new MemberWriteRequest("Sarah", "Williams-Okafor", null, "204-555-0111", "Phone", null));

        var result = await _service.UpdateMemberAsync(Williams, Sarah, draft, default);

        Assert.Equal("Williams-Okafor", result.Member!.LastName);
        Assert.Equal("Sarah Williams-Okafor", result.Member.FullName);
        Assert.Null(result.Member.Email);
        Assert.Equal("Phone", result.Member.PreferredContactMethod);
    }

    [Fact]
    public async Task A_member_of_another_household_is_not_found()
    {
        var draft = Draft(new MemberWriteRequest("Sarah", "Williams", null, null, null, null));

        var result = await _service.UpdateMemberAsync(Okafor, Sarah, draft, default);

        Assert.Equal(MemberWriteStatus.MemberNotFound, result.Status);
    }

    [Fact]
    public async Task An_unknown_household_is_not_found()
    {
        var draft = Draft(new MemberWriteRequest(null, "Nobody", null, null, null, null));

        Assert.Equal(MemberWriteStatus.HouseholdNotFound, (await _service.CreateMemberAsync("001MOCK99999999999", draft, default)).Status);
        Assert.Equal(MemberWriteStatus.HouseholdNotFound, (await _service.UpdateMemberAsync("001MOCK99999999999", Sarah, draft, default)).Status);
    }
}
