import { createElement } from 'lwc';
import OpportunityFollowUps from 'c/opportunityFollowUps';
import getFollowUps from '@salesforce/apex/OpportunityFollowUpsController.getFollowUps';
import { updateRecord } from 'lightning/uiRecordApi';
import { refreshApex } from '@salesforce/apex';

// The Apex method as a test wire adapter, so tests can emit data or an error.
jest.mock(
    '@salesforce/apex/OpportunityFollowUpsController.getFollowUps',
    () => {
        const { createApexTestWireAdapter } = require('@salesforce/sfdx-lwc-jest');
        return { default: createApexTestWireAdapter(jest.fn()) };
    },
    { virtual: true }
);
jest.mock(
    '@salesforce/apex',
    () => ({ refreshApex: jest.fn(() => Promise.resolve()) }),
    { virtual: true }
);
jest.mock('lightning/uiRecordApi', () => ({ updateRecord: jest.fn(() => Promise.resolve({})) }), { virtual: true });

const FOLLOW_UPS = [
    {
        id: '00T000000000001AAA',
        subject: 'Qualification follow-up: Williams Household - Retirement Planning',
        status: 'Not Started',
        priority: 'Normal',
        dueDate: '2026-11-12',
        isClosed: false,
        isOverdue: true,
        automation: 'Qualification follow-up'
    },
    {
        id: '00T000000000002AAA',
        subject: 'Send the fee schedule',
        status: 'In Progress',
        priority: 'High',
        dueDate: '2026-11-20',
        isClosed: false,
        isOverdue: false,
        automation: null
    },
    {
        id: '00T000000000003AAA',
        subject: 'Intro call',
        status: 'Completed',
        priority: 'Normal',
        dueDate: '2026-10-01',
        isClosed: true,
        isOverdue: false,
        automation: null
    }
];

function render() {
    const element = createElement('c-opportunity-follow-ups', { is: OpportunityFollowUps });
    element.recordId = '006000000000001AAA';
    document.body.appendChild(element);
    return element;
}

const flush = () => Promise.resolve();

describe('c-opportunity-follow-ups', () => {
    afterEach(() => {
        while (document.body.firstChild) {
            document.body.removeChild(document.body.firstChild);
        }
        jest.clearAllMocks();
    });

    it('lists follow-ups with overdue and Flow-created tags and an open/overdue count', async () => {
        const element = render();
        getFollowUps.emit(FOLLOW_UPS);
        await flush();

        const items = element.shadowRoot.querySelectorAll('[data-id="followup"]');
        expect(items).toHaveLength(3);
        expect(items[0].querySelector('[data-id="overdue"]')).not.toBeNull();
        expect(items[0].querySelector('[data-id="automation"]').label).toBe('Qualification follow-up');
        expect(items[1].querySelector('[data-id="overdue"]')).toBeNull();
        expect(items[1].textContent).toContain('High priority');
        expect(element.shadowRoot.querySelector('lightning-card').title).toBe('Follow-ups (2 open, 1 overdue)');
    });

    it('offers Mark complete only on open tasks', async () => {
        const element = render();
        getFollowUps.emit(FOLLOW_UPS);
        await flush();

        const buttons = element.shadowRoot.querySelectorAll('lightning-button');
        expect(buttons).toHaveLength(2);
        expect(buttons[0].dataset.id).toBe('00T000000000001AAA');
    });

    it('marks a task complete through Lightning Data Service and refreshes', async () => {
        const element = render();
        getFollowUps.emit(FOLLOW_UPS);
        await flush();
        const toast = jest.fn();
        element.addEventListener('lightning__showtoast', toast);

        element.shadowRoot.querySelector('lightning-button').click();
        await flush();
        await flush();

        expect(updateRecord).toHaveBeenCalledWith({ fields: { Id: '00T000000000001AAA', Status: 'Completed' } });
        expect(refreshApex).toHaveBeenCalled();
        expect(toast.mock.calls[0][0].detail.variant).toBe('success');
    });

    it('explains the empty state', async () => {
        const element = render();
        getFollowUps.emit([]);
        await flush();

        expect(element.shadowRoot.querySelector('[data-id="empty"]').textContent).toContain('Qualification');
        expect(element.shadowRoot.querySelector('lightning-card').title).toBe('Follow-ups (0 open)');
    });

    it('shows a readable error', async () => {
        const element = render();
        getFollowUps.error({ message: 'Insufficient access' });
        await flush();

        expect(element.shadowRoot.querySelector('[data-id="error"]').textContent).toContain('Insufficient access');
    });
});
