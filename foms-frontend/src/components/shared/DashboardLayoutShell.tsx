import React, { useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { GlobalHeader } from './GlobalHeader';
import { useAuth } from '../../context/AuthContext';
import { useInactivityLogout } from '../../hooks/useInactivityLogout';

// Map current route pathname to readable page header title
const ROUTE_TITLE_MAP: Record<string, { title: string; subtitle?: string }> = {
  '/foms/dashboard': { title: 'Executive Dashboard', subtitle: 'Real-time billing, receivables, cash flow, and operational metrics' },
  '/foms/approvals': { title: 'Approvals Queue', subtitle: 'Manage budget overrides and multi-level cash outflow approvals' },
  '/foms/clients': { title: 'Client Accounts & Rates', subtitle: 'Governed rate configurations, proposals, and customer billing settings' },
  '/foms/waybills': { title: 'Waybills & POD Intake', subtitle: 'Ingest DMS delivery proofs, track counterparts, and resolve exceptions' },
  '/foms/invoices': { title: 'Billing & Invoices', subtitle: 'Batch billing runs, invoice computation breakdown, and issuance' },
  '/foms/accounts-receivable': { title: 'Accounts Receivable', subtitle: 'Awaiting acknowledgment, aging buckets, and customer balances' },
  '/foms/payments': { title: 'Payment Collection & Validation', subtitle: 'Exact invoice settlements, manager validation, and exception queues' },
  '/foms/settlements': { title: 'Settlement Validation', subtitle: 'Trip liquidations, courier balances, and disbursement verification' },
  '/foms/adjustments': { title: 'Financial Adjustments', subtitle: 'Credit memos and recorded CEO-approved discounts' },
  '/foms/liquidation': { title: 'Liquidation Reports', subtitle: 'Itemized trip receipts, supervisor approvals, and discrepancy settlement' },
  '/foms/cash-flow': { title: 'Cash Flow Management', subtitle: 'Inflow and outflow tracking across bank and cash-on-hand accounts' },
  '/foms/budget': { title: 'Budget & Company Finance', subtitle: 'Monthly departmental budgets, allocation grids, and variance alerts' },
  '/foms/reports': { title: 'Consolidated Financial Reports', subtitle: 'Receivables, payments, cash flow, budgets, and profit-or-loss statement' },
  '/foms/ai-insights': { title: 'AI Decision Support', subtitle: 'Duplicate waybill detection, receivable risk scoring, and collection priorities' },
  '/foms/audit-log': { title: 'System Audit Trail', subtitle: 'Immutable, tamper-evident record of all system and financial transactions' },
  '/profile': { title: 'User Profile', subtitle: 'Current employee identity, role permissions, and active session details' },
  '/notifications': { title: 'System Notifications', subtitle: 'Real-time system alerts, due date notices, and validation updates' },
};

export const DashboardLayoutShell: React.FC = () => {
  const [collapsed, setCollapsed] = useState(false);
  const location = useLocation();
  const { logout, user } = useAuth();

  // IAM-02: 15-minute inactivity session expiration
  useInactivityLogout(() => {
    logout();
  }, Boolean(user));

  // Determine current page title
  const currentPath = location.pathname;
  const routeMeta =
    ROUTE_TITLE_MAP[currentPath] ||
    Object.entries(ROUTE_TITLE_MAP).find(([key]) => currentPath.startsWith(key))?.[1] ||
    { title: 'Finance Operations Management System', subtitle: 'Speedex OneUI Suite' };

  return (
    <div
      style={{
        display: 'flex',
        minHeight: '100vh',
        background: 'var(--bg-main)',
        color: 'var(--text-primary)',
      }}
      data-testid="foms-layout-shell"
    >
      {/* 290px/80px Left-Hand Navigation Sidebar */}
      <Sidebar collapsed={collapsed} onToggleCollapse={() => setCollapsed((prev) => !prev)} />

      {/* Main Workspace Content Area */}
      <div
        style={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          minWidth: 0,
          overflowX: 'hidden',
        }}
      >
        {/* 70px Global Topbar Header */}
        <GlobalHeader title={routeMeta.title} subtitle={routeMeta.subtitle} />

        {/* 28px Padded Main Canvas */}
        <main
          style={{
            flex: 1,
            padding: 'var(--padding-main)',
            maxWidth: 1440,
            width: '100%',
            margin: '0 auto',
            boxSizing: 'border-box',
          }}
        >
          <Outlet />
        </main>
      </div>
    </div>
  );
};

export default DashboardLayoutShell;
