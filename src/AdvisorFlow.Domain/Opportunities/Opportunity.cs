namespace AdvisorFlow.Domain.Opportunities;

/// <summary>
/// A potential advisory engagement for a household (a Salesforce Opportunity with the
/// Advisory Engagement record type). <see cref="Amount"/> is the estimated advisory service
/// revenue, not assets under management (BR-OP-003). <see cref="Probability"/> is set by
/// Salesforce from the stage and is read-only.
/// </summary>
public sealed record Opportunity(
    string Id,
    string HouseholdId,
    string HouseholdName,
    string Name,
    string Stage,
    DateOnly CloseDate,
    decimal? Amount,
    int? Probability,
    string? ServiceType,
    string? EngagementPriority,
    string? NextStep,
    string? LossReason);

/// <summary>
/// The opportunity fields an advisor sets when creating or editing an opportunity.
/// Values are already validated and use the picklists' exact spelling. The household is
/// chosen on create and cannot be changed afterwards.
/// </summary>
public sealed record OpportunityDraft(
    string Name,
    string Stage,
    DateOnly CloseDate,
    decimal? Amount,
    string? ServiceType,
    string? EngagementPriority,
    string? NextStep,
    string? LossReason);

/// <summary>
/// The AdvisorFlow Advisory sales process (blueprint §9), in pipeline order, and the
/// stage-transition rule BR-OP-004 (ADR-010).
/// </summary>
public static class OpportunityStages
{
    public const string Prospecting = "Prospecting";
    public const string Qualification = "Qualification";
    public const string NeedsAssessment = "Needs Assessment";
    public const string ProposalPresented = "Proposal Presented";
    public const string NegotiationReview = "Negotiation/Review";
    public const string ClosedWon = "Closed Won";
    public const string ClosedLost = "Closed Lost";

    /// <summary>All stages in pipeline order. The two closed stages share the last position.</summary>
    public static readonly IReadOnlyList<string> Ordered =
        [Prospecting, Qualification, NeedsAssessment, ProposalPresented, NegotiationReview, ClosedWon, ClosedLost];

    public static readonly IReadOnlySet<string> All = new HashSet<string>(Ordered, StringComparer.OrdinalIgnoreCase);

    /// <summary>Salesforce's default probability (%) for each stage, used by the mock provider.</summary>
    public static readonly IReadOnlyDictionary<string, int> DefaultProbability = new Dictionary<string, int>
    {
        [Prospecting] = 10,
        [Qualification] = 10,
        [NeedsAssessment] = 30,
        [ProposalPresented] = 60,
        [NegotiationReview] = 90,
        [ClosedWon] = 100,
        [ClosedLost] = 0,
    };

    public static bool IsClosed(string stage) => stage is ClosedWon or ClosedLost;

    /// <summary>
    /// BR-OP-004 (ADR-010): an opportunity may stay where it is, move forward any number of
    /// stages (including straight to Closed Won or Closed Lost), or move back one stage to
    /// correct a mistake. A closed opportunity is final. Both stages use their exact spelling.
    /// </summary>
    public static StageChange CheckChange(string from, string to)
    {
        if (from == to)
        {
            return StageChange.Allowed;
        }

        if (IsClosed(from))
        {
            return StageChange.ClosedIsFinal;
        }

        var step = Position(to) - Position(from);
        return step >= -1 ? StageChange.Allowed : StageChange.TooFarBack;
    }

    private static int Position(string stage) => IsClosed(stage) ? Ordered.Count - 2 : IndexOf(stage);

    private static int IndexOf(string stage)
    {
        for (var i = 0; i < Ordered.Count; i++)
        {
            if (Ordered[i] == stage)
            {
                return i;
            }
        }

        // A stage outside the AdvisorFlow process (e.g. set directly in Salesforce) counts as the start.
        return 0;
    }
}

/// <summary>Outcome of <see cref="OpportunityStages.CheckChange"/>.</summary>
public enum StageChange
{
    Allowed,

    /// <summary>The opportunity is Closed Won or Closed Lost and cannot change stage.</summary>
    ClosedIsFinal,

    /// <summary>The change moves back more than one stage.</summary>
    TooFarBack,
}

/// <summary>Approved Service Type values (Opportunity.Service_Type__c, blueprint §9).</summary>
public static class ServiceTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Retirement Planning", "Investment Advisory", "Financial Review", "Other",
    };
}

/// <summary>Approved Engagement Priority values (Opportunity.Engagement_Priority__c).</summary>
public static class EngagementPriorities
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Low", "Medium", "High",
    };
}

/// <summary>Approved Loss Reason values (Opportunity.Loss_Reason__c, BR-OP-002).</summary>
public static class LossReasons
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Chose another advisor", "Fees", "Timing", "Needs changed", "No response", "Other",
    };
}
