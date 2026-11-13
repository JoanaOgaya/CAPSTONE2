import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import PeriodClose from '../pages/PeriodClose';
import { ToastProvider } from '../components/ToastContext';
import * as AuthContextModule from '../context/AuthContext';
import { Role } from '../types/auth';

vi.mock('../services/periodCloseService', () => ({
  periodCloseService: {
    list: vi.fn().mockResolvedValue([]),
    close: vi.fn().mockResolvedValue({ id: 'period-1', periodType: 'Month' }),
    addCorrection: vi.fn().mockResolvedValue({ id: 'adj-1' }),
  },
}));

vi.mock('../services/paymentsService', () => ({
  paymentsService: {
    list: vi.fn().mockResolvedValue({ items: [], total: 0 }),
  },
}));

function mockAuth(role: Role = Role.FINANCE_MANAGER) {
  vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
    user: {
      employeeId: 'EMP-001',
      fullName: 'Crystalyn Joyce C. Fajardo',
      role,
      email: 'crystelyn.fajardo@speedex.ph',
    },
    token: 'mock-jwt-token',
    login: vi.fn(),
    logout: vi.fn(),
    isAuthenticated: true,
    isLoading: false,
  });
}

describe('PeriodClose — Speedex OneUI Interface', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders OneUI Period Close header, metric cards, and close form for Finance Manager', async () => {
    mockAuth(Role.FINANCE_MANAGER);

    render(
      <ToastProvider>
        <PeriodClose />
      </ToastProvider>
    );

    expect(screen.getByRole('heading', { level: 1, name: /Period Close/i })).toBeDefined();
    expect(screen.getByText(/Target Cycle/i)).toBeDefined();
    expect(screen.getByText(/Exceptions Queue/i)).toBeDefined();
    expect(screen.getByText(/4-Way Match Policy/i)).toBeDefined();
    expect(screen.getByText(/Archived Periods/i)).toBeDefined();

    expect(screen.getByRole('heading', { level: 2, name: /Close a period/i })).toBeDefined();
    expect(screen.getByLabelText(/Period type/i)).toBeDefined();
    expect(screen.getByLabelText(/Bank book balance/i)).toBeDefined();
    expect(screen.getByLabelText(/Bank statement closing balance/i)).toBeDefined();
    expect(screen.getByLabelText(/Bank statement proof reference/i)).toBeDefined();
    expect(screen.getByLabelText(/Cash book balance/i)).toBeDefined();
    expect(screen.getByLabelText(/Counted cash balance/i)).toBeDefined();

    expect(screen.getByRole('button', { name: /Validate checklist and close/i })).toBeDefined();
  });

  it('displays live balance variance indicator when balances are entered', async () => {
    mockAuth(Role.FINANCE_MANAGER);

    render(
      <ToastProvider>
        <PeriodClose />
      </ToastProvider>
    );

    const bankBook = screen.getByLabelText(/Bank book balance/i);
    const bankStmt = screen.getByLabelText(/Bank statement closing balance/i);

    fireEvent.change(bankBook, { target: { value: '100000' } });
    fireEvent.change(bankStmt, { target: { value: '100000' } });

    await waitFor(() => {
      expect(screen.getByText(/Balanced \(₱0\.00 variance\)/i)).toBeDefined();
    });
  });

  it('renders styled empty state for closed periods when none exist', async () => {
    mockAuth(Role.FINANCE_MANAGER);

    render(
      <ToastProvider>
        <PeriodClose />
      </ToastProvider>
    );

    expect(screen.getByRole('heading', { level: 2, name: /Closed periods/i })).toBeDefined();
    expect(screen.getByText(/No periods have been closed\./i)).toBeDefined();
  });
});
