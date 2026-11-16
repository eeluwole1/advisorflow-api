/**
 * Tests OpportunityFollowUpsController: the order, the overdue rule (BR-ACT-003) and the
 * Flow-created marker. Each test builds its own data; Salesforce rolls it back.
 */
@IsTest
private class OpportunityFollowUpsControllerTest {
    private static Opportunity createOpportunity(String stage) {
        Account household = new Account(
            Name = 'Test Household',
            RecordTypeId = Schema.SObjectType.Account.getRecordTypeInfosByDeveloperName().get('Household').getRecordTypeId()
        );
        insert household;
        Opportunity o = new Opportunity(
            Name = 'Test Household - Investment Advisory',
            AccountId = household.Id,
            RecordTypeId = Schema.SObjectType.Opportunity.getRecordTypeInfosByDeveloperName().get('Advisory_Engagement').getRecordTypeId(),
            StageName = stage,
            CloseDate = Date.today().addDays(60)
        );
        insert o;
        return o;
    }

    private static Task task(Id whatId, String subject, Date due, String status) {
        return new Task(WhatId = whatId, Subject = subject, ActivityDate = due, Status = status, Priority = 'Normal');
    }

    @IsTest
    static void listsOpenTasksFirstByDueDateWithOverdueFlagged() {
        Opportunity o = createOpportunity('Prospecting'); // no Flow task in Prospecting
        insert new List<Task>{
            task(o.Id, 'Done last month', Date.today().addDays(-30), 'Completed'),
            task(o.Id, 'No due date', null, 'Not Started'),
            task(o.Id, 'Due next week', Date.today().addDays(7), 'Not Started'),
            task(o.Id, 'Due today', Date.today(), 'In Progress'),
            task(o.Id, 'Late', Date.today().addDays(-2), 'Not Started')
        };

        Test.startTest();
        List<OpportunityFollowUpsController.FollowUp> followUps = OpportunityFollowUpsController.getFollowUps(o.Id);
        Test.stopTest();

        List<String> subjects = new List<String>();
        for (OpportunityFollowUpsController.FollowUp f : followUps) {
            subjects.add(f.subject);
        }
        Assert.areEqual(
            new List<String>{ 'Late', 'Due today', 'Due next week', 'No due date', 'Done last month' },
            subjects,
            'Open first, by due date, undated last, closed at the end'
        );
        Assert.isTrue(followUps[0].isOverdue, 'Due before today and open');
        Assert.isFalse(followUps[1].isOverdue, 'Due today is not overdue yet');
        Assert.isFalse(followUps[3].isOverdue, 'No due date is never overdue');
        Assert.isFalse(followUps[4].isOverdue, 'Closed tasks are never overdue');
        Assert.isTrue(followUps[4].isClosed);
    }

    @IsTest
    static void reportsTheFlowCreatedFollowUp() {
        Opportunity o = createOpportunity('Qualification'); // the Flow creates the follow-up

        List<OpportunityFollowUpsController.FollowUp> followUps = OpportunityFollowUpsController.getFollowUps(o.Id);

        Assert.areEqual(1, followUps.size());
        Assert.areEqual('Qualification follow-up', followUps[0].automation);
        Assert.areEqual(Date.today().addDays(3), followUps[0].dueDate);
    }

    @IsTest
    static void anOpportunityWithoutTasksReturnsAnEmptyList() {
        Opportunity o = createOpportunity('Prospecting');

        Assert.areEqual(0, OpportunityFollowUpsController.getFollowUps(o.Id).size());
    }

    @IsTest
    static void aMissingIdIsAHandledError() {
        try {
            OpportunityFollowUpsController.getFollowUps(null);
            Assert.fail('Expected an AuraHandledException');
        } catch (AuraHandledException e) {
            Assert.isNotNull(e);
        }
    }
}
