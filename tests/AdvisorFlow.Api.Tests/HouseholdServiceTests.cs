using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Households;
using AdvisorFlow.Infrastructure.Crm;

namespace AdvisorFlow.Api.Tests;

public class HouseholdServiceTests
{
    private readonly HouseholdService _service = new(new MockCrmDataProvider());

    [Fact]
    public void ValidateQuery_accepts_defaults()
    {
        var errors = HouseholdService.ValidateQuery(1, HouseholdService.DefaultPageSize, null, null);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public void ValidateQuery_rejects_bad_paging(int page, int pageSize, string field)
    {
        var errors = HouseholdService.ValidateQuery(page, pageSize, null, null);
        Assert.True(errors.ContainsKey(field));
    }

    [Fact]
    public void ValidateQuery_rejects_unapproved_status() // BR-CL-001
    {
        var errors = HouseholdService.ValidateQuery(1, 20, null, "VIP");
        Assert.True(errors.ContainsKey("status"));
    }

    [Fact]
    public void ValidateQuery_accepts_approved_status_case_insensitively()
    {
        Assert.Empty(HouseholdService.ValidateQuery(1, 20, null, "prospect"));
    }

    [Fact]
    public void ValidateQuery_rejects_overlong_search()
    {
        var errors = HouseholdService.ValidateQuery(1, 20, new string('a', HouseholdService.MaxSearchLength + 1), null);
        Assert.True(errors.ContainsKey("search"));
    }

    [Theory]
    [InlineData("001MOCK00000000001", true)]   // 18 characters
    [InlineData("001MOCK00000000", true)]      // 15 characters
    [InlineData("001MOCK0000000000", false)]   // 17 characters
    [InlineData("001' OR Name != '", false)]   // injection attempt
    [InlineData("", false)]
    public void IsValidRecordId_matches_salesforce_id_shape(string id, bool expected)
    {
        Assert.Equal(expected, HouseholdService.IsValidRecordId(id));
    }

    [Fact]
    public async Task GetHouseholds_returns_summaries_with_member_count_and_primary()
    {
        var result = await _service.GetHouseholdsAsync(new HouseholdQuery(1, 100, "Williams", null), default);

        var williams = Assert.Single(result.Items);
        Assert.Equal("Williams Household", williams.Name);
        Assert.Equal(2, williams.MemberCount);
        Assert.Equal("James Williams", williams.PrimaryContactName);
    }

    [Fact]
    public async Task GetHouseholds_pages_results()
    {
        var first = await _service.GetHouseholdsAsync(new HouseholdQuery(1, 3, null, null), default);
        var third = await _service.GetHouseholdsAsync(new HouseholdQuery(3, 3, null, null), default);

        Assert.Equal(3, first.Items.Count);
        Assert.Equal(7, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Single(third.Items);
    }

    [Fact]
    public async Task GetHouseholds_filters_by_status()
    {
        var result = await _service.GetHouseholdsAsync(new HouseholdQuery(1, 100, null, "Prospect"), default);
        Assert.All(result.Items, h => Assert.Equal(ClientStatuses.Prospect, h.ClientStatus));
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task GetHousehold_returns_null_for_unknown_id()
    {
        Assert.Null(await _service.GetHouseholdAsync("001MOCK99999999999", default));
    }

    [Fact]
    public async Task GetMembers_lists_primary_member_first() // BR-HH-003 display rule
    {
        var members = await _service.GetMembersAsync("001MOCK00000000004", default);

        Assert.NotNull(members);
        Assert.Equal(3, members.Count);
        Assert.True(members[0].IsPrimary);
        Assert.Single(members, m => m.IsPrimary);
    }

    [Fact]
    public async Task GetMembers_returns_empty_list_for_household_without_members()
    {
        var members = await _service.GetMembersAsync("001MOCK00000000007", default);
        Assert.NotNull(members);
        Assert.Empty(members);
    }

    [Fact]
    public async Task GetMembers_returns_null_for_unknown_household() // BR-HH-002
    {
        Assert.Null(await _service.GetMembersAsync("001MOCK99999999999", default));
    }
}
