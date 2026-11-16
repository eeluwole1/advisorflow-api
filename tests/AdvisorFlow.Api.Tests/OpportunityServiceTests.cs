using AdvisorFlow.Application.Opportunities;
using AdvisorFlow.Domain.Opportunities;
using AdvisorFlow.Infrastructure.Crm;

namespace AdvisorFlow.Api.Tests;

/// <summary>The stage-transition rule BR-OP-004 as decided in ADR-010.</summary>
public class OpportunityStageRuleTests
{
    [Theory]
    [InlineData("Prospecting", "Prospecting")]
    [InlineData("Prospecting", "Qualification")]
    [InlineData("Prospecting", "Negotiation/Review")]   // forward any number of stages
    [InlineData("Qualification", "Closed Won")]
    [InlineData("Prospecting", "Closed Lost")]
    [InlineData("Proposal Presented", "Needs Assessment")] // back one stage to correct a mistake
    [InlineData("Qualification", "Prospecting")]
    [InlineData("Closed Won", "Closed Won")]             // a closed opportunity can still be edited
    public void Allowed_changes(string from, string to)
    {
        Assert.Equal(StageChange.Allowed, OpportunityStages.CheckChange(from, to));
    }

    [Theory]
    [InlineData("Proposal Presented", "Qualification")]
    [InlineData("Negotiation/Review", "Prospecting")]
    public void Moving_back_more_than_one_stage_is_refused(string from, string to)
    {
        Assert.Equal(StageChange.TooFarBack, OpportunityStages.CheckChange(from, to));
    }

    [Theory]
    [InlineData("Closed Won", "Negotiation/Review")]
    [InlineData("Closed Lost", "Prospecting")]
    [InlineData("Closed Won", "Closed Lost")]
    [InlineData("Closed Lost", "Closed Won")]
    public void A_closed_opportunity_keeps_its_stage(string from, string to)
    {
        Assert.Equal(StageChange.ClosedIsFinal, OpportunityStages.CheckChange(from, to));
    }

    [Fact]
    public void The_stages_follow_the_blueprint_pipeline()
    {
        Assert.Equal(
            new[] { "Prospecting", "Qualification", "Needs Assessment", "Proposal Presented", "Negotiation/Review", "Closed Won", "Closed Lost" },
            OpportunityStages.Ordered.ToArray());
    }
}

/// <summary>Opportunity validation (BR-OP-001, BR-OP-002) and create/update workflow, with the mock provider.</summary>
public class OpportunityServiceTests
{
    private const string Williams = "001MOCK00000000001";
    private const string Martin = "001MOCK00000000007";        // no opportunities
    private const string WilliamsRetirement = "006MOCK00000000001"; // Qualification
    private const string OkaforReview = "006MOCK00000000003";       // Closed Won
    private const string Unknown = "006MOCK00000000999";

    private readonly OpportunityService _service = new(new MockCrmDataProvider());

    private static OpportunityWriteRequest Request(
        string? name = "Martin Household - Financial Review", string? stage = null, string? closeDate = "2027-03-31",
        decimal? amount = null, string? serviceType = null, string? priority = null, string? nextStep = null,
        string? lossReason = null) =>
        new(name, stage, closeDate, amount, serviceType, priority, nextStep, lossReason);

    private static OpportunityDraft Draft(OpportunityWriteRequest request, bool isCreate = true)
    {
        var errors = OpportunityService.ValidateOpportunity(request, isCreate, out var draft);
        Assert.Empty(errors);
        Assert.NotNull(draft);
        return draft;
    }

    private static string[] ErrorFields(OpportunityWriteRequest? request, bool isCreate = true) =>
        OpportunityService.ValidateOpportunity(request, isCreate, out _).Keys.Order().ToArray();

    [Fact]
    public void Values_are_trimmed_and_picklists_use_their_exact_spelling()
    {
        var draft = Draft(Request(
            name: "  Martin - Review ", stage: "needs assessment", amount: 2500.5m,
            serviceType: "financial review", priority: "HIGH", nextStep: "  Call Luc "));

        Assert.Equal(
            new OpportunityDraft("Martin - Review", "Needs Assessment", new DateOnly(2027, 3, 31), 2500.5m,
                "Financial Review", "High", "Call Luc", null),
            draft);
    }

    [Fact]
    public void Stage_defaults_to_Prospecting_on_create_only()
    {
        Assert.Equal("Prospecting", Draft(Request(stage: null)).Stage);
        Assert.Equal(new[] { "stage" }, ErrorFields(Request(stage: null), isCreate: false));
    }

    [Fact]
    public void Name_and_close_date_are_required() // BR-OP-001
    {
        Assert.Equal(new[] { "closeDate", "name" }, ErrorFields(Request(name: " ", closeDate: null)));
    }

    [Theory]
    [InlineData("31/03/2027")]
    [InlineData("2027-3-31")]
    [InlineData("2027-02-30")]
    [InlineData("1999-12-31")]
    public void Close_date_must_be_an_ISO_date_in_range(string closeDate)
    {
        Assert.Equal(new[] { "closeDate" }, ErrorFields(Request(closeDate: closeDate)));
    }

    [Theory]
    [InlineData("Value Proposition")] // a standard Salesforce stage outside the AdvisorFlow process
    [InlineData("Won")]
    public void Unknown_stages_are_rejected(string stage)
    {
        Assert.Equal(new[] { "stage" }, ErrorFields(Request(stage: stage)));
    }

    [Fact]
    public void Picklists_reject_unapproved_values()
    {
        Assert.Equal(
            new[] { "engagementPriority", "serviceType" },
            ErrorFields(Request(serviceType: "Tax Filing", priority: "Urgent")));
    }

    [Fact]
    public void Amount_must_be_a_non_negative_currency_value() // BR-OP-003: advisory revenue
    {
        Assert.Equal(new[] { "amount" }, ErrorFields(Request(amount: -1m)));
        Assert.Equal(new[] { "amount" }, ErrorFields(Request(amount: 10.555m)));
        Assert.Equal(new[] { "amount" }, ErrorFields(Request(amount: 100_000_000_000_000m)));
        Assert.Equal(0m, Draft(Request(amount: 0m)).Amount);
    }

    [Fact]
    public void Closed_Lost_requires_a_loss_reason() // BR-OP-002
    {
        Assert.Equal(new[] { "lossReason" }, ErrorFields(Request(stage: "Closed Lost")));
        Assert.Equal("Fees", Draft(Request(stage: "closed lost", lossReason: "fees")).LossReason);
    }

    [Fact]
    public void Only_Closed_Lost_may_have_a_loss_reason()
    {
        Assert.Equal(new[] { "lossReason" }, ErrorFields(Request(stage: "Closed Won", lossReason: "Fees")));
        Assert.Equal(new[] { "lossReason" }, ErrorFields(Request(stage: "Closed Lost", lossReason: "Bad luck")));
    }

    [Fact]
    public void A_missing_body_is_reported()
    {
        Assert.Equal(new[] { "body" }, ErrorFields(null));
    }

    [Fact]
    public void Query_rejects_unknown_stage_and_malformed_household_id()
    {
        var errors = OpportunityService.ValidateQuery(0, 500, null, "Won", "not-an-id");

        Assert.Equal(new[] { "householdId", "page", "pageSize", "stage" }, errors.Keys.Order().ToArray());
        Assert.Empty(OpportunityService.ValidateQuery(1, 20, null, "closed won", Williams));
    }

    [Fact]
    public async Task Pipeline_is_ordered_by_close_date_and_filters_by_stage_case_insensitively()
    {
        var all = await _service.GetOpportunitiesAsync(new OpportunityQuery(1, 100, null, null, null), default);
        Assert.Equal(7, all.TotalCount);
        Assert.Equal(all.Items.OrderBy(o => o.CloseDate).Select(o => o.Id).ToArray(), all.Items.Select(o => o.Id).ToArray());

        var won = await _service.GetOpportunitiesAsync(new OpportunityQuery(1, 20, null, "closed won", null), default);
        var only = Assert.Single(won.Items);
        Assert.Equal(OkaforReview, only.Id);
        Assert.True(only.IsClosed);
        Assert.Equal("Okafor Household", only.HouseholdName);
        Assert.Equal(100, only.Probability);
    }

    [Fact]
    public async Task Household_opportunities_are_empty_or_null_for_an_unknown_household()
    {
        Assert.Empty((await _service.GetHouseholdOpportunitiesAsync(Martin, default))!);
        Assert.Null(await _service.GetHouseholdOpportunitiesAsync("001MOCK00000000999", default));
    }

    [Fact]
    public async Task Create_needs_an_existing_household() // BR-OP-001
    {
        var result = await _service.CreateOpportunityAsync("001MOCK00000000999", Draft(Request()), default);

        Assert.Equal(OpportunityWriteStatus.HouseholdNotFound, result.Status);
    }

    [Fact]
    public async Task Create_stores_the_opportunity_with_the_stage_probability()
    {
        var result = await _service.CreateOpportunityAsync(Martin, Draft(Request(stage: "Needs Assessment", amount: 1800m)), default);

        Assert.Equal(OpportunityWriteStatus.Ok, result.Status);
        var created = result.Opportunity!;
        Assert.Equal(Martin, created.HouseholdId);
        Assert.Equal("Martin Household", created.HouseholdName);
        Assert.Equal(30, created.Probability);
        Assert.False(created.IsClosed);
        Assert.StartsWith("006MOCK", created.Id);
        Assert.Equal(created, await _service.GetOpportunityAsync(created.Id, default));
    }

    [Fact]
    public async Task Update_moves_forward_and_back_one_stage()
    {
        var forward = await _service.UpdateOpportunityAsync(
            WilliamsRetirement, Draft(Request(name: "Williams - Retirement", stage: "Proposal Presented"), isCreate: false), default);
        Assert.Equal(OpportunityWriteStatus.Ok, forward.Status);
        Assert.Equal(60, forward.Opportunity!.Probability);

        var back = await _service.UpdateOpportunityAsync(
            WilliamsRetirement, Draft(Request(name: "Williams - Retirement", stage: "Needs Assessment"), isCreate: false), default);
        Assert.Equal(OpportunityWriteStatus.Ok, back.Status);
        Assert.Equal("Needs Assessment", back.Opportunity!.Stage);
    }

    [Fact]
    public async Task Update_refuses_moving_back_more_than_one_stage() // BR-OP-004
    {
        var service = new OpportunityService(new MockCrmDataProvider());
        var nguyen = "006MOCK00000000005"; // Negotiation/Review

        var result = await service.UpdateOpportunityAsync(
            nguyen, Draft(Request(stage: "Needs Assessment"), isCreate: false), default);

        Assert.Equal(OpportunityWriteStatus.TooFarBack, result.Status);
        Assert.Equal("Negotiation/Review", (await service.GetOpportunityAsync(nguyen, default))!.Stage);
    }

    [Fact]
    public async Task Update_keeps_a_closed_opportunity_closed() // BR-OP-004
    {
        var reopen = await _service.UpdateOpportunityAsync(
            OkaforReview, Draft(Request(stage: "Negotiation/Review"), isCreate: false), default);
        Assert.Equal(OpportunityWriteStatus.ClosedIsFinal, reopen.Status);

        var edit = await _service.UpdateOpportunityAsync(
            OkaforReview, Draft(Request(name: "Okafor - Annual Review", stage: "Closed Won", amount: 2600m), isCreate: false), default);
        Assert.Equal(OpportunityWriteStatus.Ok, edit.Status);
        Assert.Equal(2600m, edit.Opportunity!.Amount);
    }

    [Fact]
    public async Task Update_of_an_unknown_opportunity_is_not_found()
    {
        var result = await _service.UpdateOpportunityAsync(Unknown, Draft(Request(stage: "Prospecting"), isCreate: false), default);

        Assert.Equal(OpportunityWriteStatus.OpportunityNotFound, result.Status);
    }
}
