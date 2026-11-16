using AdvisorFlow.Application.Activities;
using AdvisorFlow.Domain.Activities;
using AdvisorFlow.Infrastructure.Crm;

namespace AdvisorFlow.Api.Tests;

/// <summary>A clock that always reads the same instant, so "today" and "overdue" are predictable.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Task rules (BR-ACT-003), query validation and the service over the mock provider (M5).</summary>
public class ActivityServiceTests
{
    // 16 Nov 2026, noon in Winnipeg (UTC-6 in November).
    private static readonly FixedClock Noon16Nov = new(new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static ActivityService ServiceAt(TimeProvider clock, TimeProvider? seededAt = null) =>
        new(new MockCrmDataProvider(seededAt ?? clock), clock);

    [Theory]
    [InlineData(false, "2026-11-15", true)]   // open, due yesterday
    [InlineData(false, "2026-11-16", false)]  // due today is not overdue yet
    [InlineData(false, "2026-11-17", false)]
    [InlineData(true, "2026-11-01", false)]   // closed tasks are never overdue
    [InlineData(false, null, false)]          // no due date, never overdue
    public void Overdue_means_open_and_due_before_today(bool isClosed, string? due, bool expected)
    {
        DateOnly? dueDate = due is null ? null : DateOnly.Parse(due);
        Assert.Equal(expected, ActivityRules.IsOverdue(isClosed, dueDate, Today));
    }

    [Fact]
    public void Today_follows_Winnipeg_time_not_UTC()
    {
        // 03:00 UTC on 17 Nov is still 21:00 on 16 Nov in Winnipeg.
        var lateEvening = new FixedClock(new DateTimeOffset(2026, 11, 17, 3, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 11, 16), ActivityService.TodayFor(lateEvening));
    }

    [Fact]
    public void List_order_is_open_first_then_due_date_with_undated_last()
    {
        ActivityTask T(string id, bool closed, DateOnly? due, string subject = "Task") =>
            new(id, subject, closed ? "Completed" : "Not Started", null, due, closed, "001A", "A", null, null, null, null, null, null);

        var ordered = ActivityRules.InListOrder(
        [
            T("1", closed: true, Today.AddDays(-9)),
            T("2", closed: false, null),
            T("3", closed: false, Today.AddDays(5)),
            T("4", closed: false, Today.AddDays(-2)),
            T("5", closed: false, Today.AddDays(5), "Another"),
        ]).Select(t => t.Id).ToArray();

        Assert.Equal(["4", "5", "3", "2", "1"], ordered);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("OVERDUE")]
    [InlineData(" closed ")]
    [InlineData("all")]
    [InlineData((string?)null)]
    public void Known_states_are_accepted_in_any_case(string? state)
    {
        Assert.Empty(ActivityService.ValidateQuery(1, 20, null, state, null));
    }

    [Fact]
    public void Unknown_state_and_bad_household_id_are_field_errors()
    {
        var errors = ActivityService.ValidateQuery(1, 20, null, "later", "not-an-id");
        Assert.Equal(["householdId", "state"], errors.Keys.Order().ToArray());
    }

    [Fact]
    public async Task Default_list_is_open_tasks_with_overdue_flagged()
    {
        var page = await ServiceAt(Noon16Nov).GetActivitiesAsync(1, 20, null, null, null, CancellationToken.None);

        Assert.Equal(6, page.TotalCount);
        Assert.All(page.Items, t => Assert.False(t.IsClosed));
        Assert.Equal(2, page.Items.Count(t => t.IsOverdue));
        Assert.True(page.Items[0].IsOverdue);              // overdue tasks sort to the top
        Assert.Null(page.Items[^1].DueDate);                // undated last
    }

    [Fact]
    public async Task Overdue_list_uses_the_service_clock()
    {
        // Seeded on 16 Nov, read five days later: two more open tasks have passed their due date.
        var fiveDaysLater = new FixedClock(new DateTimeOffset(2026, 11, 21, 18, 0, 0, TimeSpan.Zero));
        var page = await ServiceAt(fiveDaysLater, seededAt: Noon16Nov)
            .GetActivitiesAsync(1, 20, null, "overdue", null, CancellationToken.None);

        Assert.Equal(4, page.TotalCount);
        Assert.All(page.Items, t => Assert.True(t.IsOverdue));
    }

    [Fact]
    public async Task Closed_and_search_filters_combine()
    {
        var service = ServiceAt(Noon16Nov);

        var closed = await service.GetActivitiesAsync(1, 20, null, "closed", null, CancellationToken.None);
        Assert.Equal(2, closed.TotalCount);
        Assert.All(closed.Items, t => Assert.True(t.IsClosed));

        var search = await service.GetActivitiesAsync(1, 20, "review", "all", null, CancellationToken.None);
        Assert.Equal(["Proposal review: Okafor Household - Investment Advisory", "Send the annual review summary"],
            search.Items.Select(t => t.Subject).ToArray());
    }

    [Fact]
    public async Task Household_tasks_include_its_opportunities_and_closed_tasks()
    {
        var tasks = await ServiceAt(Noon16Nov).GetHouseholdActivitiesAsync("001MOCK00000000002", CancellationToken.None);

        Assert.NotNull(tasks);
        Assert.Equal(3, tasks.Count);
        Assert.Equal(["Household", "Opportunity", "Opportunity"], tasks.Select(t => t.RelatedToType!).ToArray());
        Assert.True(tasks[^1].IsClosed);
    }

    [Fact]
    public async Task Flow_created_tasks_carry_their_automation()
    {
        var tasks = await ServiceAt(Noon16Nov).GetOpportunityActivitiesAsync("006MOCK00000000001", CancellationToken.None);

        var task = Assert.Single(tasks!);
        Assert.Equal(TaskAutomations.QualificationFollowUp, task.Automation);
        Assert.Equal("Williams Household - Retirement Planning", task.RelatedToName);
    }

    [Fact]
    public async Task Empty_household_has_an_empty_list_and_unknown_records_are_null()
    {
        var service = ServiceAt(Noon16Nov);

        Assert.Empty((await service.GetHouseholdActivitiesAsync("001MOCK00000000007", CancellationToken.None))!);
        Assert.Null(await service.GetHouseholdActivitiesAsync("001MOCK00000000099", CancellationToken.None));
        Assert.Null(await service.GetOpportunityActivitiesAsync("006MOCK00000000099", CancellationToken.None));
    }
}
