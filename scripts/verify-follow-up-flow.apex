// Verifies the AdvisorFlow_Opportunity_Follow_Ups flow (BR-ACT-001, BR-ACT-002, ADR-011).
// Run:  sf apex run --target-org advisorflow-dev --file scripts/verify-follow-up-flow.apex
// Everything it creates is rolled back at the end, so the org is left unchanged.

Boolean check(String label, Id oppId, Integer expectQualification, Integer expectProposal, List<String> failures) {
    Integer q = [SELECT COUNT() FROM Task WHERE WhatId = :oppId AND Automation__c = 'Qualification follow-up'];
    Integer p = [SELECT COUNT() FROM Task WHERE WhatId = :oppId AND Automation__c = 'Proposal review'];
    Boolean ok = q == expectQualification && p == expectProposal;
    System.debug(LoggingLevel.ERROR, (ok ? 'PASS ' : 'FAIL ') + label
        + ' -> qualification tasks ' + q + ' (expected ' + expectQualification + ')'
        + ', proposal tasks ' + p + ' (expected ' + expectProposal + ')');
    if (!ok) failures.add(label);
    return ok;
}

void stage(Opportunity o, String stageName) {
    o.StageName = stageName;
    update o;
}

Savepoint sp = Database.setSavepoint();
List<String> failures = new List<String>();
try {
    Id rt = [SELECT Id FROM RecordType WHERE SobjectType = 'Opportunity' AND DeveloperName = 'Advisory_Engagement'].Id;
    Id household = [SELECT Id FROM Account WHERE Name = 'Carter Household' LIMIT 1].Id;

    Opportunity o = new Opportunity(Name = 'Flow test - Carter', AccountId = household, RecordTypeId = rt,
        StageName = 'Prospecting', CloseDate = Date.today().addDays(60));
    insert o;
    check('1 created in Prospecting', o.Id, 0, 0, failures);

    o.NextStep = 'Call to book discovery';
    update o;
    check('2 edit without a stage change', o.Id, 0, 0, failures);

    stage(o, 'Qualification');
    check('3 enters Qualification', o.Id, 1, 0, failures);

    Task t = [SELECT Subject, ActivityDate, OwnerId, Status FROM Task WHERE WhatId = :o.Id AND Automation__c = 'Qualification follow-up'];
    Id owner = [SELECT OwnerId FROM Opportunity WHERE Id = :o.Id].OwnerId;
    Boolean details = t.Subject == 'Qualification follow-up: Flow test - Carter'
        && t.ActivityDate == Date.today().addDays(3) && t.OwnerId == owner && t.Status == 'Not Started';
    System.debug(LoggingLevel.ERROR, (details ? 'PASS ' : 'FAIL ') + '3b task details -> "' + t.Subject
        + '", due ' + String.valueOf(t.ActivityDate) + ', status ' + t.Status + ', owned by opportunity owner: ' + (t.OwnerId == owner));
    if (!details) failures.add('3b task details');

    o.NextStep = 'Send intake questionnaire';
    update o;
    check('4 edit while in Qualification', o.Id, 1, 0, failures);

    stage(o, 'Prospecting');
    stage(o, 'Qualification');
    check('5 back and forward with the task still open', o.Id, 1, 0, failures);

    t.Status = 'Completed';
    update t;
    stage(o, 'Prospecting');
    stage(o, 'Qualification');
    check('6 back and forward after completing the task', o.Id, 2, 0, failures);

    stage(o, 'Needs Assessment');
    stage(o, 'Proposal Presented');
    check('7 enters Proposal Presented', o.Id, 2, 1, failures);

    stage(o, 'Closed Won');
    check('8 closed won creates nothing', o.Id, 2, 1, failures);

    Opportunity direct = new Opportunity(Name = 'Flow test - Carter direct', AccountId = household, RecordTypeId = rt,
        StageName = 'Qualification', CloseDate = Date.today().addDays(60));
    insert direct;
    check('9 created directly in Qualification', direct.Id, 1, 0, failures);
} catch (Exception e) {
    failures.add('exception');
    System.debug(LoggingLevel.ERROR, 'FAIL exception -> ' + e.getMessage());
} finally {
    Database.rollback(sp);
}
System.debug(LoggingLevel.ERROR, 'RESULT ' + (failures.isEmpty() ? 'ALL PASSED' : failures.size() + ' FAILED: ' + String.join(failures, ', '))
    + ' (test records rolled back)');
