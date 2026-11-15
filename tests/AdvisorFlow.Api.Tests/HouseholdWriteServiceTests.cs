using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;
using AdvisorFlow.Infrastructure.Crm;

namespace AdvisorFlow.Api.Tests;

/// <summary>Household create/update rules (M3): validation, cleaning, and the mock provider's writes.</summary>
public class HouseholdWriteServiceTests
{
    private readonly HouseholdService _service = new(new MockCrmDataProvider());

    private static HouseholdDraft ValidDraft(HouseholdWriteRequest request, bool isCreate = true)
    {
        var errors = HouseholdService.ValidateHousehold(request, isCreate, out var draft);
        Assert.Empty(errors);
        Assert.NotNull(draft);
        return draft;
    }

    private static string[] ErrorFields(HouseholdWriteRequest? request, bool isCreate = true) =>
        HouseholdService.ValidateHousehold(request, isCreate, out _).Keys.Order().ToArray();

    [Fact]
    public void Create_with_only_a_name_defaults_to_Prospect() // BR-CL-001 default
    {
        var draft = ValidDraft(new HouseholdWriteRequest("Okafor Household", null, null, null, null));

        Assert.Equal(new HouseholdDraft("Okafor Household", ClientStatuses.Prospect, null, null, null), draft);
    }

    [Fact]
    public void Values_are_trimmed_and_picklists_use_their_exact_spelling()
    {
        var draft = ValidDraft(new HouseholdWriteRequest("  Okafor Household ", "active", "moderate", "HIGH", " 204-555-0199 "));

        Assert.Equal(new HouseholdDraft("Okafor Household", "Active", "Moderate", "High", "204-555-0199"), draft);
    }

    [Fact]
    public void Empty_optional_fields_are_cleared()
    {
        var draft = ValidDraft(new HouseholdWriteRequest("Okafor Household", "Inactive", " ", "", "  "), isCreate: false);

        Assert.Null(draft.RiskProfile);
        Assert.Null(draft.Priority);
        Assert.Null(draft.Phone);
    }

    [Fact]
    public void Update_requires_a_client_status()
    {
        Assert.Equal(new[] { "clientStatus" }, ErrorFields(new HouseholdWriteRequest("Okafor Household", null, null, null, null), isCreate: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string? name)
    {
        Assert.Equal(new[] { "name" }, ErrorFields(new HouseholdWriteRequest(name, null, null, null, null)));
    }

    [Fact]
    public void Too_long_values_are_rejected()
    {
        var request = new HouseholdWriteRequest(new string('a', 256), null, null, null, new string('1', 41));

        Assert.Equal(new[] { "name", "phone" }, ErrorFields(request));
    }

    [Fact]
    public void Unapproved_picklist_values_are_rejected() // BR-CL-001
    {
        var request = new HouseholdWriteRequest("Okafor Household", "VIP", "Reckless", "Urgent", null);

        Assert.Equal(new[] { "clientStatus", "priority", "riskProfile" }, ErrorFields(request));
    }

    [Fact]
    public void A_missing_body_is_rejected()
    {
        Assert.Equal(new[] { "body" }, ErrorFields(null));
    }

    [Fact]
    public async Task Created_household_can_be_read_and_found()
    {
        var draft = ValidDraft(new HouseholdWriteRequest("Okafor-Bello Household", "Active", null, "Low", null));

        var created = await _service.CreateHouseholdAsync(draft, default);

        Assert.Matches("^001MOCK[0-9]{11}$", created.Id);
        Assert.Equal(0, created.MemberCount);
        var detail = await _service.GetHouseholdAsync(created.Id, default);
        Assert.NotNull(detail);
        Assert.Equal("Okafor-Bello Household", detail.Name);
        var page = await _service.GetHouseholdsAsync(new HouseholdQuery(1, 20, "bello", null), default);
        Assert.Equal(created.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Update_replaces_editable_fields_and_keeps_the_account_number()
    {
        var draft = ValidDraft(new HouseholdWriteRequest("Williams Family", "Inactive", null, null, null), isCreate: false);

        var updated = await _service.UpdateHouseholdAsync("001MOCK00000000001", draft, default);

        Assert.NotNull(updated);
        Assert.Equal("Williams Family", updated.Name);
        Assert.Equal("Inactive", updated.ClientStatus);
        Assert.Null(updated.RiskProfile);
        Assert.Null(updated.Phone);
        Assert.Equal("ADV-0001", updated.AccountNumber);
        Assert.Equal(2, updated.MemberCount);
    }

    [Fact]
    public async Task Updating_an_unknown_household_returns_null()
    {
        var draft = ValidDraft(new HouseholdWriteRequest("Nobody", "Active", null, null, null), isCreate: false);

        Assert.Null(await _service.UpdateHouseholdAsync("001MOCK99999999999", draft, default));
    }

    [Fact]
    public async Task Each_mock_provider_starts_from_the_seed_data()
    {
        var draft = ValidDraft(new HouseholdWriteRequest("Extra Household", null, null, null, null));
        await _service.CreateHouseholdAsync(draft, default);

        var fresh = new HouseholdService(new MockCrmDataProvider());
        var page = await fresh.GetHouseholdsAsync(new HouseholdQuery(1, 20, null, null), default);

        Assert.Equal(7, page.TotalCount);
    }
}
