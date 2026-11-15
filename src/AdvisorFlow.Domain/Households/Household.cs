namespace AdvisorFlow.Domain.Households;

/// <summary>
/// A household (a Salesforce Account under the household model decision).
/// RiskProfile is a CRM classification tag only. It is not an investment
/// suitability assessment.
/// </summary>
public sealed record Household(
    string Id,
    string Name,
    string ClientStatus,
    string? RiskProfile,
    string? Priority,
    string? Phone,
    string? AccountNumber);

/// <summary>An individual household member (a Salesforce Contact) with one primary household.</summary>
public sealed record HouseholdMember(
    string Id,
    string HouseholdId,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? PreferredContactMethod,
    bool IsPrimary)
{
    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>Approved Client Status values (BR-CL-001). Verify against the Salesforce picklist during M2.</summary>
public static class ClientStatuses
{
    public const string Prospect = "Prospect";
    public const string Active = "Active";
    public const string Inactive = "Inactive";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Prospect, Active, Inactive };
}

/// <summary>Approved Client Risk Profile values (verified against the Salesforce picklist, M2).</summary>
public static class RiskProfiles
{
    public const string Conservative = "Conservative";
    public const string Moderate = "Moderate";
    public const string Aggressive = "Aggressive";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Conservative, Moderate, Aggressive };
}

/// <summary>Approved Customer Priority values (verified against the Salesforce picklist, M2).</summary>
public static class Priorities
{
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { High, Medium, Low };
}

/// <summary>
/// The household fields an advisor sets when creating or editing a household.
/// Values are already validated and use the picklists' exact spelling.
/// Deactivating a household means setting <see cref="ClientStatus"/> to Inactive (ADR-008).
/// </summary>
public sealed record HouseholdDraft(
    string Name,
    string ClientStatus,
    string? RiskProfile,
    string? Priority,
    string? Phone);
