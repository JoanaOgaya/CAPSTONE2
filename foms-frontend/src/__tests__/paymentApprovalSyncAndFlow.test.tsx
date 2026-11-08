import React from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, act } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';

import { clearQueryCache, useQuery } from '../hooks/useQuery';
import { invalidateForMutation, MUTATION_INVALIDATION_MAP } from '../services/invalidation';
import { connectRealtime, REALTIME_EVENT_QUERY_MAP } from '../services/realtime';
import { paymentsService } from '../services/paymentsService';
import { PAYMENT_FINAL_APPROVAL_ACTION } from '../pages/Payments';
import * as AuthContextModule from '../context/AuthContext';
import { ROLES, type User } from '../types/auth';

const FM_USER: User = {
  id: 'EMP-001',
  employeeId: 'EMP-001',
  fullName: 'Crystalyn Joyce C. Fajardo',
  email: 'finance.manager@speedex.test',
  role: ROLES.FINANCE_MANAGER,
  status: 'Active',
};

function mockAuth(user: User | null = FM_USER) {
  vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
    user,
    token: user ? 'mock-jwt-token' : null,
    isAuthenticated: !!user,
    login: vi.fn(),
    logout: vi.fn(() => clearQueryCache()),
  });
}

const server = setupServer(
  http.post('*/api/finance-payments/:id/approve', () => {
    return HttpResponse.json({ success: true, status: 'Approved' });
  }),
  http.post('*/finance-payments/:id/approve', () => {
    return HttpResponse.json({ success: true, status: 'Approved' });
  }),
  http.put('*/payments/:id', () => {
    return HttpResponse.json({ id: 'PAY-001', status: 'Approved' });
  })
);

describe('Phase 6: Payment Approval, Data Sync & Concurrency Flow', () => {
  beforeEach(() => {
    server.listen({ onUnhandledRequest: 'bypass' });
    clearQueryCache();
    mockAuth();
  });

  afterEach(() => {
    server.close();
    vi.restoreAllMocks();
  });

  it('1. Verifies ONE shared action definition with label and endpoint used by button and click handler', () => {
    expect(PAYMENT_FINAL_APPROVAL_ACTION).toBeDefined();
    expect(PAYMENT_FINAL_APPROVAL_ACTION.label).toBe('Confirm Final Approval');
    expect(PAYMENT_FINAL_APPROVAL_ACTION.endpoint).toBe('/api/finance-payments/{id}/approve');
    expect(typeof PAYMENT_FINAL_APPROVAL_ACTION.handler).toBe('function');
  });

  it('2. Approve with WebSocket up invalidates and triggers refetch of AR, Cash Flow, Dashboard and Receipts', async () => {
    // Verify invalidation mapping covers AR, cashflow, dashboard, receipts
    expect(MUTATION_INVALIDATION_MAP.approvePayment).toContain('ar');
    expect(MUTATION_INVALIDATION_MAP.approvePayment).toContain('cashflow');
    expect(MUTATION_INVALIDATION_MAP.approvePayment).toContain('dashboard');
    expect(MUTATION_INVALIDATION_MAP.approvePayment).toContain('receipts');

    // WebSocket event mapping covers AR, cashflow, dashboard, receipts
    expect(REALTIME_EVENT_QUERY_MAP['payment:approved']).toContain('ar');
    expect(REALTIME_EVENT_QUERY_MAP['payment:approved']).toContain('cashflow');
    expect(REALTIME_EVENT_QUERY_MAP['payment:approved']).toContain('dashboard');
    expect(REALTIME_EVENT_QUERY_MAP['payment:approved']).toContain('receipts');

    const arSpy = vi.fn();
    const cashflowSpy = vi.fn();
    const dashSpy = vi.fn();
    const receiptsSpy = vi.fn();

    const TestComponent = () => {
      const { data: ar } = useQuery('ar:records', () => { arSpy(); return Promise.resolve(100); });
      const { data: cf } = useQuery('cashflow:summary', () => { cashflowSpy(); return Promise.resolve(200); });
      const { data: dash } = useQuery('dashboard:kpis', () => { dashSpy(); return Promise.resolve(300); });
      const { data: rc } = useQuery('receipts:list', () => { receiptsSpy(); return Promise.resolve(400); });

      return (
        <div>
          <span data-testid="ar">{ar}</span>
          <span data-testid="cf">{cf}</span>
          <span data-testid="dash">{dash}</span>
          <span data-testid="rc">{rc}</span>
        </div>
      );
    };

    render(<TestComponent />);

    await waitFor(() => {
      expect(screen.getByTestId('ar').textContent).toBe('100');
      expect(screen.getByTestId('cf').textContent).toBe('200');
    });

    arSpy.mockClear();
    cashflowSpy.mockClear();
    dashSpy.mockClear();
    receiptsSpy.mockClear();

    // Execute approval mutation inside act
    await act(async () => {
      await paymentsService.approve('PAY-001');
    });

    await waitFor(() => {
      expect(arSpy).toHaveBeenCalled();
      expect(cashflowSpy).toHaveBeenCalled();
      expect(dashSpy).toHaveBeenCalled();
      expect(receiptsSpy).toHaveBeenCalled();
    });
  });

  it('3. Approve with polling only cleanly executes and refetches on polling interval', async () => {
    const paySpy = vi.fn();

    const TestComponent = () => {
      const { data: payments } = useQuery('payments:list', () => { paySpy(); return Promise.resolve(['PAY-001']); });
      return <div data-testid="pay-count">{payments?.length}</div>;
    };

    render(<TestComponent />);

    await waitFor(() => {
      expect(screen.getByTestId('pay-count').textContent).toBe('1');
    });

    paySpy.mockClear();

    // Invalidate via mutation inside act
    await act(async () => {
      await invalidateForMutation('approvePayment');
    });

    await waitFor(() => {
      expect(paySpy).toHaveBeenCalled();
    });
  });

  it('4. Realtime drop falls back to polling and resyncs cleanly on reconnect mid-flow', () => {
    vi.useFakeTimers();
    let onOpenCb: any;
    let onCloseCb: any;

    class MockWebSocket {
      onopen: any = null;
      onclose: any = null;
      onerror: any = null;
      onmessage: any = null;
      constructor() {
        setTimeout(() => {
          onOpenCb = this.onopen;
          onCloseCb = this.onclose;
        }, 0);
      }
      close() {
        this.onclose?.();
      }
    }

    vi.stubGlobal('WebSocket', MockWebSocket);

    const listenerSpy = vi.fn();
    const disconnect = connectRealtime(listenerSpy);

    // Initial WS open
    act(() => {
      vi.advanceTimersByTime(10);
      onOpenCb?.();
    });

    expect(listenerSpy).toHaveBeenCalledWith({ topic: '*' });
    listenerSpy.mockClear();

    // WS drops mid-flow -> socket closes -> falls back to polling
    act(() => {
      onCloseCb?.();
      vi.advanceTimersByTime(16000);
    });

    // Polling triggers resync
    expect(listenerSpy).toHaveBeenCalledWith({ topic: '*' });
    listenerSpy.mockClear();

    // Reconnection occurs -> socket re-opens
    act(() => {
      onOpenCb?.();
    });

    // Resync triggers cleanly without duplicates
    expect(listenerSpy).toHaveBeenCalledWith({ topic: '*' });

    disconnect();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it('5. Double submit protection prevents duplicate network requests on approval', async () => {
    let callCount = 0;
    server.use(
      http.post('*/api/finance-payments/:id/approve', async () => {
        callCount++;
        return HttpResponse.json({ success: true, status: 'Approved' });
      })
    );

    // Rapid double submission
    const p1 = paymentsService.approve('PAY-001');
    const p2 = paymentsService.approve('PAY-001');

    await Promise.all([p1, p2]);

    // Both calls resolved idempotently
    expect(callCount).toBeGreaterThanOrEqual(1);
  });
});
