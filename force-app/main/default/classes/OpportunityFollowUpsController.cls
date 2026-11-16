/**
 * Serves the opportunityFollowUps Lightning Web Component on the Opportunity record page (M5).
 *
 * Runs with the user's sharing and field access (with sharing, WITH USER_MODE), so a user only sees
 * the tasks and fields they may see. "Overdue" follows BR-ACT-003: open and due before today in the
 * user's time zone; a task due today is not overdue, and one with no due date never is.
 */
public with sharing class OpportunityFollowUpsController {
    /** The task list is capped; an opportunity has a handful of follow-ups in practice. */
    @TestVisible
    private static final Integer MAX_TASKS = 50;

    public class FollowUp {
        @AuraEnabled public Id id;
        @AuraEnabled public String subject;
        @AuraEnabled public String status;
        @AuraEnabled public String priority;
        @AuraEnabled public Date dueDate;
        @AuraEnabled public Boolean isClosed;
        @AuraEnabled public Boolean isOverdue;
        @AuraEnabled public String automation;
    }

    /**
     * The opportunity's tasks: open before closed, then by due date with undated tasks last
     * (the same order as the AdvisorFlow API, ADR-012).
     */
    @AuraEnabled(cacheable=true)
    public static List<FollowUp> getFollowUps(Id opportunityId) {
        if (opportunityId == null) {
            throw new AuraHandledException('An opportunity id is required.');
        }

        Date today = Date.today();
        List<FollowUp> result = new List<FollowUp>();
        for (Task t : [
            SELECT Id, Subject, Status, Priority, ActivityDate, IsClosed, Automation__c
            FROM Task
            WHERE WhatId = :opportunityId
            WITH USER_MODE
            ORDER BY IsClosed ASC, ActivityDate ASC NULLS LAST, Subject ASC, Id ASC
            LIMIT :MAX_TASKS
        ]) {
            FollowUp f = new FollowUp();
            f.id = t.Id;
            f.subject = t.Subject;
            f.status = t.Status;
            f.priority = t.Priority;
            f.dueDate = t.ActivityDate;
            f.isClosed = t.IsClosed;
            f.isOverdue = !t.IsClosed && t.ActivityDate != null && t.ActivityDate < today;
            f.automation = t.Automation__c;
            result.add(f);
        }
        return result;
    }
}
