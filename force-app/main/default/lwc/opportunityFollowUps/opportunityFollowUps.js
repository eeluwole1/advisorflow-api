import { LightningElement, api, wire } from 'lwc';
import { refreshApex } from '@salesforce/apex';
import { updateRecord } from 'lightning/uiRecordApi';
import { ShowToastEvent } from 'lightning/platformShowToastEvent';
import getFollowUps from '@salesforce/apex/OpportunityFollowUpsController.getFollowUps';

/**
 * Follow-ups card for the Opportunity record page (M5). Lists the opportunity's tasks, open first,
 * with overdue ones flagged (BR-ACT-003) and Flow-created ones tagged (ADR-011). "Mark complete"
 * saves through Lightning Data Service, so the user's own permissions apply, then refreshes the list.
 */
export default class OpportunityFollowUps extends LightningElement {
    @api recordId;

    followUps = [];
    error;
    loaded = false;
    completingId;
    wiredFollowUps;

    @wire(getFollowUps, { opportunityId: '$recordId' })
    receive(result) {
        this.wiredFollowUps = result;
        const { data, error } = result;
        if (data) {
            this.followUps = data;
            this.error = undefined;
            this.loaded = true;
        } else if (error) {
            this.followUps = [];
            this.error = messageOf(error);
            this.loaded = true;
        }
    }

    get items() {
        return this.followUps.map((f) => ({
            ...f,
            hasDueDate: Boolean(f.dueDate),
            duePrefix: f.isClosed ? 'Was due' : 'Due',
            itemClass: f.isClosed ? 'slds-item followup followup_closed' : 'slds-item followup',
            dueClass: f.isOverdue ? 'slds-text-color_error slds-text-title_bold' : '',
            isHighPriority: f.priority === 'High',
            canComplete: !f.isClosed,
            completing: this.completingId === f.id
        }));
    }

    get hasFollowUps() {
        return this.followUps.length > 0;
    }

    get isEmpty() {
        return this.loaded && !this.error && this.followUps.length === 0;
    }

    get cardTitle() {
        const open = this.followUps.filter((f) => !f.isClosed).length;
        const overdue = this.followUps.filter((f) => f.isOverdue).length;
        if (!this.loaded || this.error) {
            return 'Follow-ups';
        }
        return overdue > 0 ? `Follow-ups (${open} open, ${overdue} overdue)` : `Follow-ups (${open} open)`;
    }

    async handleComplete(event) {
        const id = event.currentTarget.dataset.id;
        this.completingId = id;
        try {
            await updateRecord({ fields: { Id: id, Status: 'Completed' } });
            this.dispatchEvent(
                new ShowToastEvent({ title: 'Follow-up completed', message: 'The task is marked Completed.', variant: 'success' })
            );
            await refreshApex(this.wiredFollowUps);
        } catch (error) {
            this.dispatchEvent(
                new ShowToastEvent({ title: "Couldn't complete the follow-up", message: messageOf(error), variant: 'error' })
            );
        } finally {
            this.completingId = undefined;
        }
    }
}

/** A readable message from an Apex or Lightning Data Service error. */
function messageOf(error) {
    if (Array.isArray(error?.body)) {
        return error.body.map((e) => e.message).join(', ');
    }
    return error?.body?.message ?? error?.message ?? 'Unknown error';
}
