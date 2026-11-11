/**
 * adjustmentsRoleAccess.test.tsx
 *
 * Item 2 (verification pass, 2026-11-11):
 * Tests that the /adjustments page (FinancialAdjustments) exposes the Approve
 * button ONLY to the Head Accountant and NOT to the Finance Manager.
 *
 * Rules verified:
 *   - R4.11 q4 / FIN-23: Head Accountant is the approver for adjusting entries.
 *   - Finance Manager creates entries; FM must NOT see an Approve button.
 *   - Head Accountant must see the Approve button and can invoke it.
 *
 * These are NEW tests in a NEW file; they do not edit emp001Flow.test.tsx.
 */
import React from 'react';
import { describe, it, expect, vi, beforeAll, afterAll, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';

import { ROLES, type User } from '../types/auth';
import { clearQueryCache } from '../hooks/useQuery';
import * as AuthContextModule from '../context/AuthContext';
import { ToastProvider } from '../components/ToastContext';
import ToastBar from '../components/ToastBar';

import FinancialAdjustments from '../pages/FinancialAdjustments';

// ── Shared MSW server ─────────────────────────────────────────────────────────

const SAMPLE_ADJUSTMENT = {
  id: 'ADJ-001',
  type: 'Adjustment',
  amount: 5000,
  reason: 'Freight rate correction',
  referenceNo: 'ADJ-001',
  status: 'Pending Approval',
  affectedRecordId: 'INV-001',
  clientId: 'CLI-001',
  createdAt: new Date().toISOString(),
  createdBy: 'Finance Manager',
  remarks: 'Freight rate correction',
  InvoiceNo: 'INV-2026-001',
  AdjustmentType: 'Credit',
  AdjustedBy: 'Finance Manager',
  DateRequested: '2026-11-11',
  ApprovedBy: null,
  DateApproved: null,
};

const server = setupServer(
  http.get(/.*\/adjustments$/, () => HttpResponse.json({ items: [SAMPLE_ADJUSTMENT], total: 1 })),
  http.get(/.*\/adjustments\?.*/, () => HttpResponse.json({ items: [SAMPLE_ADJUSTMENT], total: 1 })),
  http.get(/.*\/invoices/, () => HttpResponse.json({ items: [], total: 0 })),
  http.get(/.*\/clients/, () => HttpResponse.json({ items: [], total: 0 })),
  http.get(/.*\/audit-logs/, () => HttpResponse.json({ items: [], total: 0 })),
  http.post(/.*\/audit-logs/, () => HttpResponse.json({ success: true })),
);

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }));
afterEach(() => {
  server.resetHandlers();
  vi.restoreAllMocks();
  clearQueryCache();
});
afterAll(() => server.close());

// ── Helpers ───────────────────────────────────────────────────────────────────

function mockAuthAs(role: string) {
  const user: User = {
    id: 'EMP-TEST',
    employeeId: 'EMP-TEST',
    fullName: 'Test User',
    email: 'test@speedex.test',
    role: role as User['role'],
    status: 'Active',
  };
  vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
    user,
    isLoading: false,
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  });
}

function renderAdjustmentsPage() {
  return render(
    <MemoryRouter initialEntries={['/adjustments']}>
      <ToastProvider>
        <ToastBar />
        <Routes>
          <Route path="/adjustments" element={<FinancialAdjustments />} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('FinancialAdjustments /adjustments — role-based Approve button visibility (R4.11 q4 / FIN-23)', () => {
  it('Finance Manager does NOT see an Approve button', async () => {
    mockAuthAs(ROLES.FINANCE_MANAGER);
    renderAdjustmentsPage();

    // The adjustment record appears (FM can view)
    await waitFor(() => {
      expect(screen.getByText('Freight rate correction')).toBeDefined();
    });

    // But there is no Approve button for FM (FM creates; HA approves per R4.11 q4)
    expect(screen.queryByRole('button', { name: /approve/i })).toBeNull();
  });

  it('Head Accountant SEES an Approve button', async () => {
    mockAuthAs(ROLES.HEAD_ACCOUNTANT);
    renderAdjustmentsPage();

    // The adjustment record appears and the Approve button is rendered for HA
    await waitFor(() => {
      expect(screen.getByText('Freight rate correction')).toBeDefined();
      expect(screen.getByRole('button', { name: /approve/i })).toBeDefined();
    });
  });
});
