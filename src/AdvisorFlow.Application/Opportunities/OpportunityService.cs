using System.Globalization;
using AdvisorFlow.Application.Abstractions;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Domain.Opportunities;

namespace AdvisorFlow.Application.Opportunities;

/// <summary>
/// Application workflow for advisory opportunities (M4): validates requests against
/// BR-OP-001 to BR-OP-004, calls the provider, and maps domain records to the API DTOs.
/// </summary>
public sealed class OpportunityService(ICrmDataProvider provider)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 80;

    /// <summary>Salesforce Opportunity Name and Next Step field lengths.</summary>
    public const int MaxNameLength = 120;
    public const int MaxNextStepLength = 255;

    /// <summary>Opportunity.Amount is Currency(16, 2): up to 14 digits before the decimal point.</summary>
    public const decimal MaxAmount = 99_999_999_999_999.99m;

    private static readonly DateOnly EarliestCloseDate = new(2000, 1, 1);
    private static readonly DateOnly LatestCloseDate = new(2100, 12, 31);

    /// <summary>Returns field errors; an empty dictionary means the query is valid.</summary>
    public static Dictionary<string, string[]> ValidateQuery(
        int page, int pageSize, string? search, string? stage, string? householdId)
    {
        var errors = HouseholdService.ValidateQuery(page, pageSize, search, status: null);

        if (!string.IsNullOrWhiteSpace(stage) && !OpportunityStages.All.Contains(stage.Trim()))
        {
            errors["stage"] = [$"Stage must be one of: {string.Join(", ", OpportunityStages.Ordered)}."];
        }

        if (!string.IsNullOrWhiteSpace(householdId) && !HouseholdService.IsValidRecordId(householdId))
        {
            errors["householdId"] = ["Household id must be a 15- or 18-character Salesforce record id."];
        }

        return errors;
    }

    /// <summary>
    /// Validates a create (<paramref name="isCreate"/>) or update request and, when it is valid,
    /// returns the cleaned values: text trimmed, empty optional fields cleared, picklist values in
    /// their exact spelling. Every opportunity has a stage and a close date (BR-OP-001); on create
    /// a missing stage defaults to Prospecting, on update it is required because PUT replaces every
    /// editable field. Closed Lost needs a loss reason, and only Closed Lost may have one (BR-OP-002).
    /// </summary>
    public static Dictionary<string, string[]> ValidateOpportunity(
        OpportunityWriteRequest? request, bool isCreate, out OpportunityDraft? draft)
    {
        draft = null;
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["body"] = ["A JSON body with the opportunity fields is required."];
            return errors;
        }

        var name = Optional(request.Name);
        if (name is null)
        {
            errors["name"] = ["Name is required."];
        }
        else if (name.Length > MaxNameLength)
        {
            errors["name"] = [$"Name must be {MaxNameLength} characters or fewer."];
        }

        string? stage;
        if (string.IsNullOrWhiteSpace(request.Stage))
        {
            stage = isCreate ? OpportunityStages.Prospecting : null;
            if (stage is null)
            {
                errors["stage"] = ["Stage is required."];
            }
        }
        else
        {
            stage = Canonical(OpportunityStages.Ordered, request.Stage, "stage", errors);
        }

        var closeDate = ParseCloseDate(request.CloseDate, errors);

        var amount = request.Amount;
        if (amount is < 0)
        {
            errors["amount"] = ["Amount cannot be negative."];
        }
        else if (amount > MaxAmount)
        {
            errors["amount"] = ["Amount is too large."];
        }
        else if (amount is { } value && decimal.Round(value, 2) != value)
        {
            errors["amount"] = ["Amount can have at most 2 decimal places."];
        }

        var serviceType = Canonical(ServiceTypes.All, request.ServiceType, "serviceType", errors);
        var priority = Canonical(EngagementPriorities.All, request.EngagementPriority, "engagementPriority", errors);

        var nextStep = Optional(request.NextStep);
        if (nextStep is { Length: > MaxNextStepLength })
        {
            errors["nextStep"] = [$"Next step must be {MaxNextStepLength} characters or fewer."];
        }

        var lossReason = Canonical(LossReasons.All, request.LossReason, "lossReason", errors);
        if (!errors.ContainsKey("lossReason") && stage is not null)
        {
            if (stage == OpportunityStages.ClosedLost && lossReason is null)
            {
                errors["lossReason"] = ["A loss reason is required when the stage is Closed Lost."];
            }
            else if (stage != OpportunityStages.ClosedLost && lossReason is not null)
            {
                errors["lossReason"] = ["A loss reason can only be set when the stage is Closed Lost."];
            }
        }

        if (errors.Count == 0)
        {
            draft = new OpportunityDraft(
                name!, stage!, closeDate!.Value, amount, serviceType, priority, nextStep, lossReason);
        }

        return errors;
    }

    public async Task<PagedResponse<OpportunityDto>> GetOpportunitiesAsync(OpportunityQuery query, CancellationToken ct)
    {
        var stage = string.IsNullOrWhiteSpace(query.Stage)
            ? null
            : OpportunityStages.Ordered.First(s => string.Equals(s, query.Stage.Trim(), StringComparison.OrdinalIgnoreCase));
        var page = await provider.GetOpportunitiesAsync(query with { Stage = stage }, ct);
        var items = page.Items.Select(ToDto).ToList();
        return new PagedResponse<OpportunityDto>(items, page.Page, page.PageSize, page.TotalCount, page.TotalPages);
    }

    public async Task<OpportunityDto?> GetOpportunityAsync(string id, CancellationToken ct)
    {
        var opportunity = await provider.GetOpportunityAsync(id, ct);
        return opportunity is null ? null : ToDto(opportunity);
    }

    /// <summary>A household's opportunities, or null when the household does not exist.</summary>
    public async Task<IReadOnlyList<OpportunityDto>?> GetHouseholdOpportunitiesAsync(string householdId, CancellationToken ct)
    {
        var opportunities = await provider.GetHouseholdOpportunitiesAsync(householdId, ct);
        return opportunities?.Select(ToDto).ToList();
    }

    /// <summary>Creates an opportunity for an existing household (BR-OP-001).</summary>
    public async Task<OpportunityWriteResult> CreateOpportunityAsync(
        string householdId, OpportunityDraft draft, CancellationToken ct)
    {
        if (await provider.GetHouseholdAsync(householdId, ct) is null)
        {
            return new OpportunityWriteResult(OpportunityWriteStatus.HouseholdNotFound);
        }

        var created = await provider.CreateOpportunityAsync(householdId, draft, ct);
        return new OpportunityWriteResult(OpportunityWriteStatus.Ok, ToDto(created));
    }

    /// <summary>
    /// Replaces an opportunity's editable fields. The stage may move forward any number of
    /// stages or back one; a closed opportunity keeps its stage (BR-OP-004, ADR-010).
    /// </summary>
    public async Task<OpportunityWriteResult> UpdateOpportunityAsync(
        string id, OpportunityDraft draft, CancellationToken ct)
    {
        var current = await provider.GetOpportunityAsync(id, ct);
        if (current is null)
        {
            return new OpportunityWriteResult(OpportunityWriteStatus.OpportunityNotFound);
        }

        switch (OpportunityStages.CheckChange(current.Stage, draft.Stage))
        {
            case StageChange.ClosedIsFinal:
                return new OpportunityWriteResult(OpportunityWriteStatus.ClosedIsFinal);
            case StageChange.TooFarBack:
                return new OpportunityWriteResult(OpportunityWriteStatus.TooFarBack);
        }

        var updated = await provider.UpdateOpportunityAsync(id, draft, ct);
        return updated is null
            ? new OpportunityWriteResult(OpportunityWriteStatus.OpportunityNotFound)
            : new OpportunityWriteResult(OpportunityWriteStatus.Ok, ToDto(updated));
    }

    private static OpportunityDto ToDto(Opportunity o) => new(
        o.Id, o.HouseholdId, o.HouseholdName, o.Name, o.Stage, OpportunityStages.IsClosed(o.Stage),
        o.CloseDate, o.Amount, o.Probability, o.ServiceType, o.EngagementPriority, o.NextStep, o.LossReason);

    private static DateOnly? ParseCloseDate(string? value, Dictionary<string, string[]> errors)
    {
        var text = Optional(value);
        if (text is null)
        {
            errors["closeDate"] = ["Close date is required."];
            return null;
        }

        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors["closeDate"] = ["Close date must be a date in the format yyyy-MM-dd."];
            return null;
        }

        if (date < EarliestCloseDate || date > LatestCloseDate)
        {
            errors["closeDate"] = ["Close date must be between 2000-01-01 and 2100-12-31."];
            return null;
        }

        return date;
    }

    /// <summary>Trimmed text, or null when empty (PUT clears empty optional fields).</summary>
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The picklist's exact spelling of <paramref name="value"/>, null when empty,
    /// or an error when the value is not approved.</summary>
    private static string? Canonical(
        IEnumerable<string> allowed, string? value, string field, Dictionary<string, string[]> errors)
    {
        var trimmed = Optional(value);
        if (trimmed is null)
        {
            return null;
        }

        var match = allowed.FirstOrDefault(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            errors[field] = [$"Must be one of: {string.Join(", ", allowed)}."];
        }

        return match;
    }
}
