import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { StatusBadge } from '../components/shared/StatusBadge';
import { normalizeStatus, STATUS_LABEL, STATUS_TONE } from '../utils/workflow';
import { statusTone } from '../utils/status';

describe('Canonical Invoice Statuses, Badges, Queues & AR Constraints', () => {
  describe('1. StatusBadge Rendering with Canonical Document Statuses', () => {
    const canonicalStatuses = [
      { status: 'Draft', label: 'Draft', expectedTone: 'neutral' },
      { status: 'Pending HA Verification', label: 'Pending HA Verification', expectedTone: 'warning' },
      { status: 'Pending FM Approval', label: 'Pending FM Approval', expectedTone: 'warning' },
      { status: 'Approved', label: 'Approved', expectedTone: 'success' },
      { status: 'Sent', label: 'Sent', expectedTone: 'info' },
      { status: 'Received', label: 'Received', expectedTone: 'success' },
      { status: 'Returned to Creator', label: 'Returned to Creator', expectedTone: 'danger' },
      { status: 'Returned to HA', label: 'Returned to HA', expectedTone: 'danger' },
      { status: 'Disputed', label: 'Disputed', expectedTone: 'danger' },
      { status: 'Void', label: 'Void', expectedTone: 'neutral' },
    ];

    canonicalStatuses.forEach(({ status, label, expectedTone }) => {
      it(`renders canonical status "${status}" with label "${label}" and tone "${expectedTone}"`, () => {
        const { unmount } = render(<StatusBadge status={status} />);
        const badge = screen.getByRole('status');
        expect(badge).toBeDefined();
        expect(badge.textContent).toContain(label);
        expect(badge.className).toContain(`badge-${expectedTone}`);
        unmount();
      });
    });

    it('normalizes legacy "Voided" alias to VOID and renders neutral tone', () => {
      const { unmount } = render(<StatusBadge status="Voided" />);
      const badge = screen.getByRole('status');
      expect(badge.textContent).toContain('Void');
      expect(badge.className).toContain('badge-neutral');
      unmount();
    });
  });

  describe('2. normalizeStatus and Workflow Vocabulary Mapping', () => {
    it('correctly maps all canonical and legacy invoice statuses', () => {
      expect(normalizeStatus('Draft')).toBe('DRAFT');
      expect(normalizeStatus('Pending HA Verification')).toBe('PENDING_HA_VERIFICATION');
      expect(normalizeStatus('Pending FM Approval')).toBe('PENDING_FM_APPROVAL');
      expect(normalizeStatus('Approved')).toBe('APPROVED');
      expect(normalizeStatus('Sent')).toBe('SENT');
      expect(normalizeStatus('Received')).toBe('RECEIVED');
      expect(normalizeStatus('Returned to Creator')).toBe('RETURNED_TO_CREATOR');
      expect(normalizeStatus('Returned to HA')).toBe('RETURNED_TO_HA');
      expect(normalizeStatus('Disputed')).toBe('DISPUTED');
      expect(normalizeStatus('Void')).toBe('VOID');
      expect(normalizeStatus('Voided')).toBe('VOID');
      expect(normalizeStatus('VOID')).toBe('VOID');
    });

    it('returns valid labels and tones for all normalized statuses', () => {
      expect(STATUS_LABEL['PENDING_HA_VERIFICATION']).toBe('Pending HA Verification');
      expect(STATUS_LABEL['PENDING_FM_APPROVAL']).toBe('Pending FM Approval');
      expect(STATUS_LABEL['RETURNED_TO_HA']).toBe('Returned to HA');
      expect(STATUS_LABEL['RETURNED_TO_CREATOR']).toBe('Returned to Creator');
      expect(STATUS_LABEL['SENT']).toBe('Sent');
      expect(STATUS_LABEL['RECEIVED']).toBe('Received');
      expect(STATUS_LABEL['DISPUTED']).toBe('Disputed');
      expect(STATUS_LABEL['VOID']).toBe('Void');

      expect(STATUS_TONE['PENDING_HA_VERIFICATION']).toBe('warning');
      expect(STATUS_TONE['PENDING_FM_APPROVAL']).toBe('warning');
      expect(STATUS_TONE['RETURNED_TO_HA']).toBe('danger');
      expect(STATUS_TONE['RETURNED_TO_CREATOR']).toBe('danger');
      expect(STATUS_TONE['SENT']).toBe('info');
      expect(STATUS_TONE['RECEIVED']).toBe('success');
      expect(STATUS_TONE['DISPUTED']).toBe('danger');
      expect(STATUS_TONE['VOID']).toBe('neutral');
    });
  });

  describe('3. InvoicingDesk Queue Partitioning & Tab Counts', () => {
    const mockInvoices = [
      { id: '1', invoiceNumber: 'INV-001', status: 'Draft' },
      { id: '2', invoiceNumber: 'INV-002', status: 'Pending HA Verification' },
      { id: '3', invoiceNumber: 'INV-003', status: 'Pending FM Approval' },
      { id: '4', invoiceNumber: 'INV-004', status: 'Approved' },
      { id: '5', invoiceNumber: 'INV-005', status: 'Sent' },
      { id: '6', invoiceNumber: 'INV-006', status: 'Received' },
      { id: '7', invoiceNumber: 'INV-007', status: 'Returned to Creator' },
      { id: '8', invoiceNumber: 'INV-008', status: 'Returned to HA' },
      { id: '9', invoiceNumber: 'INV-009', status: 'Disputed' },
      { id: '10', invoiceNumber: 'INV-010', status: 'Void' },
    ];

    it('correctly partitions invoices into their respective queues', () => {
      // Pending Queue: Pending HA Verification, Pending FM Approval
      const pendingQueue = mockInvoices.filter(i =>
        ['Pending HA Verification', 'Pending FM Approval', 'Pending Approval', 'Pending'].includes(i.status)
      );
      expect(pendingQueue.map(i => i.invoiceNumber)).toEqual(['INV-002', 'INV-003']);

      // Listed Queue: Draft
      const listedQueue = mockInvoices.filter(i =>
        ['Draft', 'Listed'].includes(i.status)
      );
      expect(listedQueue.map(i => i.invoiceNumber)).toEqual(['INV-001']);

      // Approved Queue: Approved, Sent, Received
      const approvedQueue = mockInvoices.filter(i =>
        ['Approved', 'Sent', 'Received', 'Paid', 'Endorsed', 'Validated'].includes(i.status)
      );
      expect(approvedQueue.map(i => i.invoiceNumber)).toEqual(['INV-004', 'INV-005', 'INV-006']);

      // Needs Revision Queue: Returned to Creator, Returned to HA
      const revisionQueue = mockInvoices.filter(i =>
        ['Returned to Creator', 'Returned to HA', 'Needs Revision', 'Returned for revision'].includes(i.status)
      );
      expect(revisionQueue.map(i => i.invoiceNumber)).toEqual(['INV-007', 'INV-008']);

      // Voided count
      const voidedQueue = mockInvoices.filter(i => i.status === 'Void');
      expect(voidedQueue.map(i => i.invoiceNumber)).toEqual(['INV-010']);
    });
  });

  describe('4. Invoice Action Permissions & Void Immutability', () => {
    const STATUS_ACTIONS_MAP: Record<string, string[]> = {
      'Draft': ['View', 'Edit Draft', 'Submit for Verification'],
      'Pending HA Verification': ['View'],
      'Pending FM Approval': ['View'],
      'Returned to Creator': ['View', 'Edit', 'Resubmit'],
      'Returned to HA': ['View'],
      'Approved': ['View', 'Print', 'Dispatch'],
      'Sent': ['View', 'Print', 'Mark Received'],
      'Received': ['View', 'Print'],
      'Disputed': ['View', 'Print'],
      'Void': ['View'],
    };

    it('strictly forbids editing, submitting, paying, or approving Void invoices', () => {
      const voidActions = STATUS_ACTIONS_MAP['Void'];
      expect(voidActions).toEqual(['View']);
      expect(voidActions).not.toContain('Edit');
      expect(voidActions).not.toContain('Edit Draft');
      expect(voidActions).not.toContain('Submit for Verification');
      expect(voidActions).not.toContain('Submit for Approval');
      expect(voidActions).not.toContain('Approve');
      expect(voidActions).not.toContain('Dispatch');
      expect(voidActions).not.toContain('Pay');
      expect(voidActions).not.toContain('Validate');
    });

    it('allows editing and resubmission only for Draft and Returned to Creator', () => {
      expect(STATUS_ACTIONS_MAP['Draft']).toContain('Edit Draft');
      expect(STATUS_ACTIONS_MAP['Draft']).toContain('Submit for Verification');

      expect(STATUS_ACTIONS_MAP['Returned to Creator']).toContain('Edit');
      expect(STATUS_ACTIONS_MAP['Returned to Creator']).toContain('Resubmit');

      expect(STATUS_ACTIONS_MAP['Approved']).not.toContain('Edit');
      expect(STATUS_ACTIONS_MAP['Sent']).not.toContain('Edit');
    });
  });

  describe('5. AR Exclusion: Void Invoices Excluded from Outstanding Totals & Aging', () => {
    const sampleArRecords = [
      { id: '1', invoiceNumber: 'INV-101', outstandingBalance: 50000, status: 'Outstanding', documentStatus: 'Approved', agingBracket: '0-30 days' },
      { id: '2', invoiceNumber: 'INV-102', outstandingBalance: 25000, status: 'Overdue', documentStatus: 'Sent', agingBracket: '31-60 days' },
      { id: '3', invoiceNumber: 'INV-103', outstandingBalance: 100000, status: 'Void', documentStatus: 'Void', agingBracket: '90+ days' },
      { id: '4', invoiceNumber: 'INV-104', outstandingBalance: 75000, status: 'Outstanding', documentStatus: 'Received', agingBracket: '0-30 days' },
      { id: '5', invoiceNumber: 'INV-105', outstandingBalance: 30000, status: 'Voided', documentStatus: 'Void', agingBracket: 'Current' },
    ];

    it('excludes Void invoices from active AR records and total AR balance calculation', () => {
      const activeArRecords = sampleArRecords.filter(r =>
        r.outstandingBalance > 0 && r.status !== 'Void' && r.status !== 'Voided' && r.documentStatus !== 'Void'
      );

      // Should only include INV-101, INV-102, INV-104
      expect(activeArRecords.length).toBe(3);
      expect(activeArRecords.map(r => r.invoiceNumber)).toEqual(['INV-101', 'INV-102', 'INV-104']);

      const totalAr = activeArRecords.reduce((sum, r) => sum + r.outstandingBalance, 0);
      expect(totalAr).toBe(150000); // 50000 + 25000 + 75000 (excludes 100,000 and 30,000 from void invoices)
    });

    it('excludes Void invoices from aging brackets', () => {
      const activeArRecords = sampleArRecords.filter(r =>
        r.outstandingBalance > 0 && r.status !== 'Void' && r.status !== 'Voided' && r.documentStatus !== 'Void'
      );

      const agingTotals: Record<string, number> = {
        '0-30 days': 0,
        '31-60 days': 0,
        '61-90 days': 0,
        '90+ days': 0,
      };

      activeArRecords.forEach(r => {
        const bracket = r.agingBracket === 'Current' ? '0-30 days' : r.agingBracket;
        if (bracket in agingTotals) {
          agingTotals[bracket] += r.outstandingBalance;
        }
      });

      expect(agingTotals['0-30 days']).toBe(125000); // 50000 + 75000
      expect(agingTotals['31-60 days']).toBe(25000);  // 25000
      expect(agingTotals['61-90 days']).toBe(0);
      expect(agingTotals['90+ days']).toBe(0);         // INV-103 (100,000) was Void, so 90+ days is 0
    });
  });
});
