import React from 'react';
import { describe, it, expect, vi, beforeAll, afterAll, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import { CeoApprovalConfirmationSection } from '../components/approvals/CeoApprovalConfirmationSection';
import * as AuthContextModule from '../context/AuthContext';
import { ROLES, type User } from '../types/auth';

const haUser: User = {
  id: 'USER-HA-001',
  employeeId: 'EMP-002',
  fullName: 'Hannah HeadAccountant',
  role: ROLES.HEAD_ACCOUNTANT,
  email: 'ha@foms.local',
};

const fmUser: User = {
  id: 'USER-FM-001',
  employeeId: 'EMP-001',
  fullName: 'Fiona FinanceManager',
  role: ROLES.FINANCE_MANAGER,
  email: 'fm@foms.local',
};

const accUser: User = {
  id: 'USER-ACC-001',
  employeeId: 'EMP-003',
  fullName: 'Alice Accountant',
  role: ROLES.ACCOUNTANT,
  email: 'acc@foms.local',
};

const afmUser: User = {
  id: 'USER-AFM-001',
  employeeId: 'EMP-004',
  fullName: 'Arthur AsstFM',
  role: ROLES.ASSISTANT_FM,
  email: 'afm@foms.local',
};

const mockCeoApprovals = [
  {
    recordId: 101,
    itemType: 'BudgetTransfer',
    itemId: '201',
    status: 'Approved',
    approverLabel: 'CEO/President (in person)',
    enteredBy: 'EMP-001',
    enteredAt: '2026-11-10T10:00:00Z',
    approvalDate: '2026-11-10T00:00:00Z',
    notes: 'Q4 Ops reallocation',
    confirmations: [],
  },
  {
    recordId: 102,
    itemType: 'BudgetCarryOver',
    itemId: '202',
    status: 'Approved',
    approverLabel: 'CEO/President (in person)',
    enteredBy: 'EMP-001',
    enteredAt: '2026-11-10T11:00:00Z',
    approvalDate: '2026-11-10T00:00:00Z',
    notes: 'Year-end carryover',
    confirmations: [],
  },
];

let confirmPayload: any = null;

const server = setupServer(
  http.get(/.*\/ceo-approvals/, () => {
    return HttpResponse.json(mockCeoApprovals);
  }),
  http.post(/.*\/ceo-approvals\/\d+\/confirm/, async ({ request }) => {
    confirmPayload = await request.json();
    return HttpResponse.json({
      confirmationId: 1,
      recordId: 101,
      kind: confirmPayload?.kind || 'Recording',
      confirmedBy: 'EMP-002',
      confirmedAt: new Date().toISOString(),
    });
  })
);

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }));
afterEach(() => {
  server.resetHandlers();
  confirmPayload = null;
  vi.restoreAllMocks();
});
afterAll(() => server.close());

function mockAuth(user: User | null) {
  vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
    user,
    isLoading: false,
    isAuthenticated: !!user,
    login: vi.fn(),
    logout: vi.fn(),
  });
}

describe('R5.11 Item 1: Head Accountant Maker-Checker CEO Approval Confirmation', () => {
  it('Head Accountant sees the Confirm action, can enter a note, and calls the confirmation endpoint', async () => {
    mockAuth(haUser);

    render(<CeoApprovalConfirmationSection initialItems={mockCeoApprovals} />);

    // Verify both items rendered
    expect(screen.getByText('ID: 201')).toBeDefined();
    expect(screen.getByText('ID: 202')).toBeDefined();

    // Head Accountant sees the Confirm buttons
    const confirmButtons = screen.getAllByRole('button', { name: /Confirm Recording/i });
    expect(confirmButtons).toHaveLength(2);

    // Click confirm on record 101
    fireEvent.click(confirmButtons[0]);

    // Modal opens
    expect(screen.getByTestId('confirm-modal')).toBeDefined();
    expect(screen.getByText(/Confirm CEO Approval Recording/i)).toBeDefined();

    // Fill optional note
    const noteInput = screen.getByTestId('confirm-note-input');
    fireEvent.change(noteInput, { target: { value: 'Verified with signed board minute' } });

    // Submit confirmation
    const submitBtn = screen.getByTestId('submit-confirm-btn');
    fireEvent.click(submitBtn);

    // Verify endpoint called with correct payload
    await waitFor(() => {
      expect(confirmPayload).not.toBeNull();
      expect(confirmPayload.kind).toBe('Recording');
      expect(confirmPayload.note).toBe('Verified with signed board minute');
    });

    // Success feedback displayed
    await waitFor(() => {
      expect(screen.getByText(/confirmed successfully/i)).toBeDefined();
    });
  });

  it('Finance Manager does NOT see the Confirm Recording action', () => {
    mockAuth(fmUser);

    render(<CeoApprovalConfirmationSection initialItems={mockCeoApprovals} />);

    expect(screen.getByText('ID: 201')).toBeDefined();
    expect(screen.getByText('ID: 202')).toBeDefined();

    // Confirm button is absent
    const confirmButton = screen.queryByRole('button', { name: /Confirm Recording/i });
    expect(confirmButton).toBeNull();
  });

  it('Accountant does NOT see the Confirm Recording action', () => {
    mockAuth(accUser);

    render(<CeoApprovalConfirmationSection initialItems={mockCeoApprovals} />);

    expect(screen.getByText('ID: 201')).toBeDefined();
    expect(screen.getByText('ID: 202')).toBeDefined();

    // Confirm button is absent
    const confirmButton = screen.queryByRole('button', { name: /Confirm Recording/i });
    expect(confirmButton).toBeNull();
  });

  it('Assistant of Finance Manager does NOT see the Confirm Recording action', () => {
    mockAuth(afmUser);

    render(<CeoApprovalConfirmationSection initialItems={mockCeoApprovals} />);

    expect(screen.getByText('ID: 201')).toBeDefined();
    expect(screen.getByText('ID: 202')).toBeDefined();

    // Confirm button is absent
    const confirmButton = screen.queryByRole('button', { name: /Confirm Recording/i });
    expect(confirmButton).toBeNull();
  });

  it('displays "Correction" badge on corrected records and Head Accountant confirms with Kind Recording', async () => {
    mockAuth(haUser);

    const correctedItem = {
      recordId: 205,
      itemType: 'BudgetTransfer',
      itemId: 'TR-CORR-01',
      status: 'Approved',
      approverLabel: 'CEO / President',
      enteredBy: 'EMP-001',
      enteredAt: '2026-11-10T12:00:00Z',
      approvalDate: '2026-11-10T11:00:00Z',
      isCorrection: true,
      correctionReason: 'Corrected transfer amount and department code',
      confirmations: [],
    };

    let confirmRequestBody: any = null;
    server.use(
      http.post(/.*\/ceo-approvals\/205\/confirm/, async ({ request }) => {
        confirmRequestBody = await request.json();
        return HttpResponse.json({
          confirmationId: 99,
          recordId: 205,
          kind: 'Recording',
          confirmedBy: 'EMP-002',
          confirmedAt: new Date().toISOString(),
        });
      })
    );

    render(<CeoApprovalConfirmationSection initialItems={[correctedItem]} />);

    // 1. Correction badge is displayed
    const badge = screen.getByTestId('correction-badge-205');
    expect(badge).toBeDefined();
    expect(badge.textContent).toBe('Correction');

    // 2. Head Accountant clicks Confirm Recording
    const confirmBtn = screen.getByTestId('confirm-btn-205');
    fireEvent.click(confirmBtn);

    // 3. Modal opens and confirms
    const submitBtn = screen.getByTestId('submit-confirm-btn');
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(confirmRequestBody).not.toBeNull();
      expect(confirmRequestBody.kind).toBe('Recording');
    });
  });
});
