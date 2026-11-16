/**
 * Tests the record-triggered flow AdvisorFlow_Opportunity_Follow_Ups (BR-ACT-001, BR-ACT-002, ADR-011).
 *
 * When an Advisory Engagement opportunity is created in, or its stage changes into, Qualification or
 * Proposal Presented, the flow creates one task for the opportunity owner, due in 3 days, unless an
 * open task from the same automation already exists. Edits that don't change the stage, and closed
 * stages, create nothing. Each test builds its own household and opportunity; Salesforce rolls all
 * test data back.
 *
 * Every test runs as an advisor (an active System Administrator, which is what advisors are in this
 * org), not as whoever starts the run: CI runs tests as a minimum-access deploy user that may not
 * create Accounts or Opportunities at all (ADR-014).
 */
@IsTest
private class OpportunityFollowUpFlowTest {
    private static final String QUALIFICATION_FOLLOW_UP = 'Qualification follow-up';
    private static final String PROPOSAL_REVIEW = 'Proposal review';

    private static Id householdRecordTypeId() {
        return Schema.SObjectType.Account.getRecordTypeInfosByDeveloperName().get('Household').getRecordTypeId();
    }

    private static Id advisoryRecordTypeId() {
        return Schema.SObjectType.Opportunity.getRecordTypeInfosByDeveloperName().get('Advisory_Engagement').getRecordTypeId();
    }

    private static User advisor() {
        return [SELECT Id FROM User WHERE Profile.Name = 'System Administrator' AND IsActive = true LIMIT 1];
    }

    @TestSetup
    static void createHousehold() {
        System.runAs(advisor()) {
            insert new Account(Name = 'Test Household', RecordTypeId = householdRecordTypeId());
        }
    }

    private static Id householdId() {
        return [SELECT Id FROM Account WHERE Name = 'Test Household' LIMIT 1].Id;
    }

    /** Builds an opportunity without querying, so it is safe to call in a loop. */
    private static Opportunity newOpportunity(Id householdId, String name, String stage) {
        return new Opportunity(
            Name = name,
            AccountId = householdId,
            RecordTypeId = advisoryRecordTypeId(),
            StageName = stage,
            CloseDate = Date.today().addDays(60)
        );
    }

    private static Opportunity insertOpportunity(String stage) {
        Opportunity o = newOpportunity(householdId(), 'Test Household - Retirement Planning', stage);
        insert o;
        return o;
    }

    private static void moveTo(Opportunity o, String stage) {
        o.StageName = stage;
        update o;
    }

    private static Integer tasks(Id opportunityId, String automation) {
        return [SELECT COUNT() FROM Task WHERE WhatId = :opportunityId AND Automation__c = :automation];
    }

    @IsTest
    static void creatingInProspectingCreatesNoTask() {
        System.runAs(advisor()) {
            Opportunity o = insertOpportunity('Prospecting');

            Assert.areEqual(0, [SELECT COUNT() FROM Task WHERE WhatId = :o.Id], 'Prospecting is not a follow-up stage');
        }
    }

    @IsTest
    static void editingWithoutAStageChangeCreatesNoTask() {
        System.runAs(advisor()) {
            Opportunity o = insertOpportunity('Prospecting');

            o.NextStep = 'Call to book discovery';
            update o;

            Assert.areEqual(0, [SELECT COUNT() FROM Task WHERE WhatId = :o.Id], 'Only a stage change can trigger the flow');
        }
    }

    @IsTest
    static void enteringQualificationCreatesOneFollowUpForTheOwner() {
        System.runAs(advisor()) {
            Opportunity o = insertOpportunity('Prospecting');

            moveTo(o, 'Qualification');

            Task t = [SELECT Subject, ActivityDate, OwnerId, Status, Priority FROM Task WHERE WhatId = :o.Id AND Automation__c = :QUALIFICATION_FOLLOW_UP];
            Opportunity saved = [SELECT OwnerId FROM Opportunity WHERE Id = :o.Id];
            Assert.areEqual('Qualification follow-up: Test Household - Retirement Planning', t.Subject);
            Assert.areEqual(Date.today().addDays(3), t.ActivityDate, 'Due 3 days after the stage change');
            Assert.areEqual(saved.OwnerId, t.OwnerId, 'Assigned to the opportunity owner');
            Assert.areEqual('Not Started', t.Status);
            Assert.areEqual('Normal', t.Priority);
        }
    }

    @IsTest
    static void editingWhileInQualificationAddsNoDuplicate() {
        System.runAs(advisor()) {
            Opportunity o = insertOpportunity('Qualification');

            o.NextStep = 'Send intake questionnaire';
            update o;

            Assert.areEqual(1, tasks(o.Id, QUALIFICATION_FOLLOW_UP));
        }
    }

    @IsTest
    static void steppingBackAndForwardWithTheTaskOpenAddsNoDuplicate() {
        System.runAs(advisor()) {
            Opportunity o = insertOpportunity('Qualification');

            moveTo(o, 'Prospecting');
            moveTo(o, 'Qualification');

            Assert.areEqual(1, tasks(o.Id, QUALIFICATION_FOLLOW_UP), 'BR-ACT-002: an open follow-up already exists');
        }
    }

    @IsTest
    static void reEnteringAfterCompletingTheTaskCreatesANewOne() {
        System.runAs(advisor()) {
            Opportunity o = insertOpportunity('Qualification');
            Task first = [SELECT Id FROM Task WHERE WhatId = :o.Id AND Automation__c = :QUALIFICATION_FOLLOW_UP];
            first.Status = 'Completed';
            update first;

            moveTo(o, 'Prospecting');
            moveTo(o, 'Qualification');

            Assert.areEqual(2, tasks(o.Id, QUALIFICATION_FOLLOW_UP), 'A genuine second round of follow-up');
        }
    }

    @IsTest
    static void enteringProposalPresentedCreatesAReviewTask() {
        System.runAs(advisor()) {
            Opportunity o = insertOpportunity('Qualification');

            moveTo(o, 'Needs Assessment');
            moveTo(o, 'Proposal Presented');

            Assert.areEqual(1, tasks(o.Id, PROPOSAL_REVIEW));
            Assert.areEqual(1, tasks(o.Id, QUALIFICATION_FOLLOW_UP), 'The earlier follow-up is untouched');
        }
    }

    @IsTest
    static void closingWonCreatesNothing() {
        System.runAs(advisor()) {
            Opportunity o = insertOpportunity('Proposal Presented');

            moveTo(o, 'Closed Won');

            Assert.areEqual(1, [SELECT COUNT() FROM Task WHERE WhatId = :o.Id], 'Only the proposal review from creation');
        }
    }

    @IsTest
    static void creatingDirectlyInQualificationCreatesOneFollowUp() {
        System.runAs(advisor()) {
            Opportunity o = insertOpportunity('Qualification');

            Assert.areEqual(1, tasks(o.Id, QUALIFICATION_FOLLOW_UP));
        }
    }

    @IsTest
    static void bulkInsertOf200CreatesExactlyOneTaskEach() {
        System.runAs(advisor()) {
            Id household = householdId(); // query once, outside the loop
            List<Opportunity> batch = new List<Opportunity>();
            for (Integer i = 0; i < 200; i++) {
                batch.add(newOpportunity(household, 'Bulk Household ' + i, 'Qualification'));
            }

            Test.startTest();
            insert batch; // one transaction: the flow must stay within governor limits
            Test.stopTest();

            Set<Id> ids = new Map<Id, Opportunity>(batch).keySet();
            Assert.areEqual(200, [SELECT COUNT() FROM Task WHERE WhatId IN :ids AND Automation__c = :QUALIFICATION_FOLLOW_UP]);
        }
    }
}
