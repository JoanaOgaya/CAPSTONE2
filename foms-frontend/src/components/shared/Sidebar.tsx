import React from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import {
  LayoutDashboard,
  Users,
  FileText,
  FileCheck,
  CreditCard,
  Receipt,
  Scale,
  Wallet,
  FileSpreadsheet,
  Cpu,
  ShieldAlert,
  ClipboardCheck,
  CheckCircle,
  Truck,
  ArrowRightLeft,
  User,
  LogOut,
  ChevronLeft,
  ChevronRight,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { StatusBadge, RoleBadgeType } from './StatusBadge';

interface SidebarProps {
  collapsed: boolean;
  onToggleCollapse: () => void;
}

interface NavItemDef {
  path: string;
  label: string;
  icon: React.ComponentType<{ size?: number; strokeWidth?: number; className?: string }>;
  section?: string;
  roles?: string[];
}

// Navigation structure adhering to Capstone 2 Requirements & Condition 4
const NAV_ITEMS: NavItemDef[] = [
  // Overview
  { path: '/foms/dashboard', label: 'Dashboard', icon: LayoutDashboard, section: 'Main' },

  // Approvals (Condition 5: /foms/approvals)
  { path: '/foms/approvals', label: 'Approvals', icon: ClipboardCheck, section: 'Main', roles: ['FINANCIAL_MANAGER', 'ASSISTANT_FM'] },

  // Operations & Invoicing
  { path: '/foms/clients', label: 'Clients & Rates', icon: Users, section: 'Operations', roles: ['FINANCIAL_MANAGER', 'ACCOUNTANT'] },
  { path: '/foms/waybills', label: 'Waybills & POD', icon: Truck, section: 'Operations', roles: ['ACCOUNTANT', 'HEAD_ACCOUNTANT'] },
  { path: '/foms/invoices', label: 'Billing & Invoices', icon: FileText, section: 'Operations', roles: ['ACCOUNTANT', 'HEAD_ACCOUNTANT', 'FINANCIAL_MANAGER'] },

  // Receivables, Payments & Collections
  { path: '/foms/accounts-receivable', label: 'Accounts Receivable', icon: Scale, section: 'Receivables & Treasury', roles: ['ACCOUNTANT', 'HEAD_ACCOUNTANT', 'FINANCIAL_MANAGER'] },
  { path: '/foms/payments', label: 'Payments', icon: CreditCard, section: 'Receivables & Treasury', roles: ['ACCOUNTANT', 'HEAD_ACCOUNTANT', 'FINANCIAL_MANAGER'] },

  // Condition 4: Keep SpeedPay Validation, Official Receipts, and Settlement Validation as sidebar items (mapped as views of core modules)
  { path: '/foms/payments?view=speedpay', label: 'SpeedPay Validation', icon: CheckCircle, section: 'Receivables & Treasury', roles: ['ACCOUNTANT', 'HEAD_ACCOUNTANT', 'FINANCIAL_MANAGER'] },
  { path: '/foms/payments?view=receipts', label: 'Official Receipts', icon: Receipt, section: 'Receivables & Treasury', roles: ['ACCOUNTANT', 'FINANCIAL_MANAGER'] },
  { path: '/foms/settlements', label: 'Settlement Validation', icon: ArrowRightLeft, section: 'Receivables & Treasury', roles: ['ACCOUNTANT', 'HEAD_ACCOUNTANT', 'FINANCIAL_MANAGER'] },
  { path: '/foms/adjustments', label: 'Financial Adjustments', icon: Scale, section: 'Receivables & Treasury', roles: ['ACCOUNTANT', 'HEAD_ACCOUNTANT', 'FINANCIAL_MANAGER'] },

  // Cash Flow & Liquidation
  { path: '/foms/liquidation', label: 'Liquidation', icon: Receipt, section: 'Cash & Liquidation', roles: ['ASSISTANT_FM', 'FINANCIAL_MANAGER'] },
  { path: '/foms/cash-flow', label: 'Cash Flow', icon: Wallet, section: 'Cash & Liquidation', roles: ['FINANCIAL_MANAGER'] },
  { path: '/foms/budget', label: 'Budget & Finance', icon: FileSpreadsheet, section: 'Cash & Liquidation', roles: ['FINANCIAL_MANAGER'] },

  // Analytics, AI & Audit
  { path: '/foms/reports', label: 'Reports', icon: FileSpreadsheet, section: 'Analytics & Governance', roles: ['ACCOUNTANT', 'HEAD_ACCOUNTANT', 'FINANCIAL_MANAGER', 'ASSISTANT_FM'] },
  { path: '/foms/ai-insights', label: 'AI Decision Support', icon: Cpu, section: 'Analytics & Governance', roles: ['ACCOUNTANT', 'HEAD_ACCOUNTANT', 'FINANCIAL_MANAGER'] },
  { path: '/foms/audit-log', label: 'Audit Trail', icon: ShieldAlert, section: 'Analytics & Governance', roles: ['HEAD_ACCOUNTANT', 'FINANCIAL_MANAGER'] },
];

export const Sidebar: React.FC<SidebarProps> = ({ collapsed, onToggleCollapse }) => {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const userRole = user?.role || 'FINANCIAL_MANAGER';

  // Filter items by role
  const visibleItems = NAV_ITEMS.filter((item) => !item.roles || item.roles.includes(userRole));

  // Group items by section
  const sections = Array.from(new Set(visibleItems.map((i) => i.section || 'General')));

  const getRoleBadge = (): RoleBadgeType => {
    switch (userRole) {
      case 'FINANCIAL_MANAGER':
        return 'financial_manager';
      case 'HEAD_ACCOUNTANT':
        return 'head_accountant';
      case 'ACCOUNTANT':
        return 'accountant';
      case 'ASSISTANT_FM':
        return 'assistant_fm';
      case 'CLIENT':
        return 'client';
      default:
        return 'admin';
    }
  };

  const getUserInitials = () => {
    if (!user?.fullName) return 'SP';
    const parts = user.fullName.trim().split(/\s+/);
    if (parts.length >= 2) return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
    return parts[0].slice(0, 2).toUpperCase();
  };

  return (
    <aside
      className="sidebar-container"
      style={{
        width: collapsed ? 'var(--sidebar-collapsed)' : 'var(--sidebar-width)',
        minWidth: collapsed ? 'var(--sidebar-collapsed)' : 'var(--sidebar-width)',
        background: 'var(--sidebar-bg)',
        minHeight: '100vh',
        height: '100vh',
        position: 'sticky',
        top: 0,
        display: 'flex',
        flexDirection: 'column',
        transition: 'width var(--motion-standard) var(--ease-standard)',
        zIndex: 'var(--z-sticky)',
        overflowY: 'auto',
        overflowX: 'hidden',
        boxSizing: 'border-box',
        borderRight: '1px solid rgba(255, 255, 255, 0.05)',
      }}
      data-testid="speedex-sidebar"
    >
      {/* Brand Header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: collapsed ? 'center' : 'space-between',
          padding: collapsed ? '16px 8px' : '16px 20px',
          borderBottom: '1px solid rgba(255, 255, 255, 0.07)',
          height: 'var(--topbar-height)',
          boxSizing: 'border-box',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <div
            style={{
              width: 32,
              height: 32,
              background: 'var(--primary)',
              borderRadius: 'var(--radius-sm)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontWeight: 800,
              fontSize: '1.1rem',
              color: '#FFFFFF',
              flexShrink: 0,
            }}
          >
            S
          </div>
          {!collapsed && (
            <div>
              <span
                style={{
                  fontSize: '1.15rem',
                  fontWeight: 800,
                  color: '#FFFFFF',
                  letterSpacing: '1px',
                  display: 'block',
                  lineHeight: 1.2,
                }}
              >
                SPEEDEX
              </span>
              <span
                style={{
                  fontSize: '0.68rem',
                  fontWeight: 700,
                  color: 'rgba(255, 255, 255, 0.45)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.08em',
                }}
              >
                FOMS Enterprise
              </span>
            </div>
          )}
        </div>

        <button
          type="button"
          onClick={onToggleCollapse}
          aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          style={{
            background: 'none',
            border: 'none',
            color: 'rgba(255, 255, 255, 0.55)',
            cursor: 'pointer',
            padding: 4,
            display: 'flex',
            alignItems: 'center',
            borderRadius: 'var(--radius-xs)',
          }}
        >
          {collapsed ? <ChevronRight size={18} /> : <ChevronLeft size={18} />}
        </button>
      </div>

      {/* Navigation Sections */}
      <nav
        style={{
          flex: 1,
          padding: collapsed ? '12px 8px' : '12px 14px',
          display: 'flex',
          flexDirection: 'column',
          gap: 4,
        }}
      >
        {sections.map((sec) => {
          const itemsInSec = visibleItems.filter((i) => (i.section || 'General') === sec);
          return (
            <div key={sec} style={{ marginBottom: 10 }}>
              {!collapsed && (
                <div
                  style={{
                    fontSize: '0.68rem',
                    fontWeight: 700,
                    color: 'rgba(255, 255, 255, 0.35)',
                    textTransform: 'uppercase',
                    letterSpacing: '1px',
                    padding: '10px 12px 4px',
                    userSelect: 'none',
                  }}
                >
                  {sec}
                </div>
              )}
              {itemsInSec.map((item) => {
                const IconComponent = item.icon;
                return (
                  <NavLink
                    key={item.path}
                    to={item.path}
                    title={collapsed ? item.label : undefined}
                    style={({ isActive }) => ({
                      display: 'flex',
                      alignItems: 'center',
                      gap: 12,
                      padding: collapsed ? '10px 0' : '10px 12px',
                      justifyContent: collapsed ? 'center' : 'flex-start',
                      borderRadius: 'var(--radius-sm)',
                      color: isActive ? '#FFFFFF' : 'rgba(255, 255, 255, 0.65)',
                      backgroundColor: isActive ? 'var(--primary)' : 'transparent',
                      textDecoration: 'none',
                      fontSize: '0.88rem',
                      fontWeight: 600,
                      transition: 'background var(--motion-fast) var(--ease-standard), color var(--motion-fast) var(--ease-standard)',
                      minHeight: 40,
                      boxSizing: 'border-box',
                    })}
                    onMouseEnter={(e) => {
                      if (!e.currentTarget.classList.contains('active')) {
                        e.currentTarget.style.backgroundColor = 'rgba(255, 255, 255, 0.06)';
                        e.currentTarget.style.color = 'rgba(255, 255, 255, 0.9)';
                      }
                    }}
                    onMouseLeave={(e) => {
                      if (!e.currentTarget.classList.contains('active')) {
                        e.currentTarget.style.backgroundColor = 'transparent';
                        e.currentTarget.style.color = 'rgba(255, 255, 255, 0.65)';
                      }
                    }}
                  >
                    <span style={{ display: 'inline-flex', flexShrink: 0 }}>
                      <IconComponent size={18} strokeWidth={2} />
                    </span>
                    {!collapsed && <span>{item.label}</span>}
                  </NavLink>
                );
              })}
            </div>
          );
        })}
      </nav>

      {/* User Footer Row (Pinned to bottom) */}
      <div
        style={{
          marginTop: 'auto',
          padding: collapsed ? '14px 8px' : '14px 16px',
          borderTop: '1px solid rgba(255, 255, 255, 0.07)',
          display: 'flex',
          flexDirection: 'column',
          gap: 10,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div
            style={{
              width: 34,
              height: 34,
              borderRadius: 'var(--radius-sm)',
              background: 'var(--primary-dark)',
              color: '#FFFFFF',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontWeight: 700,
              fontSize: '0.82rem',
              flexShrink: 0,
            }}
          >
            {getUserInitials()}
          </div>
          {!collapsed && (
            <div style={{ overflow: 'hidden', flex: 1 }}>
              <div
                style={{
                  fontSize: '0.82rem',
                  fontWeight: 700,
                  color: 'rgba(255, 255, 255, 0.9)',
                  whiteSpace: 'nowrap',
                  textOverflow: 'ellipsis',
                  overflow: 'hidden',
                }}
              >
                {user?.fullName || 'Finance Staff'}
              </div>
              <div style={{ marginTop: 2 }}>
                <StatusBadge role={getRoleBadge()} status={userRole} />
              </div>
            </div>
          )}
        </div>

        {!collapsed && (
          <div style={{ display: 'flex', gap: 8, marginTop: 4 }}>
            <button
              type="button"
              onClick={() => navigate('/profile')}
              className="btn btn-outline"
              style={{
                flex: 1,
                height: 30,
                fontSize: '0.75rem',
                padding: '0 8px',
                background: 'rgba(255, 255, 255, 0.05)',
                color: 'rgba(255, 255, 255, 0.8)',
                border: '1px solid rgba(255, 255, 255, 0.15)',
              }}
            >
              <User size={14} strokeWidth={2} />
              My Profile
            </button>
            <button
              type="button"
              onClick={logout}
              className="btn btn-danger"
              style={{
                height: 30,
                fontSize: '0.75rem',
                padding: '0 10px',
              }}
              title="Log Out"
            >
              <LogOut size={14} strokeWidth={2} />
              Log Out
            </button>
          </div>
        )}
      </div>
    </aside>
  );
};

export default Sidebar;
