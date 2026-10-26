import React from 'react';

export type StatusVariant =
  | 'active'
  | 'pending'
  | 'failed'
  | 'transit'
  | 'new'
  | 'validated'
  | 'on_hold'
  | 'ctc'
  | 'draft'
  | 'approved'
  | 'sent'
  | 'received'
  | 'disputed'
  | 'void'
  | 'paid'
  | 'unpaid'
  | 'rejected'
  | 'for_correction'
  | 'late';

export type RoleBadgeType =
  | 'financial_manager'
  | 'head_accountant'
  | 'accountant'
  | 'assistant_fm'
  | 'client'
  | 'admin';

interface StatusBadgeProps {
  status?: string | null;
  variant?: StatusVariant;
  role?: RoleBadgeType;
  icon?: React.ReactNode;
  className?: string;
}

const STATUS_MAP: Record<string, { bg: string; color: string; label: string }> = {
  // Standard Statuses
  active: { bg: 'rgba(5, 150, 105, 0.1)', color: '#059669', label: 'Active' },
  validated: { bg: 'rgba(5, 150, 105, 0.1)', color: '#059669', label: 'Validated' },
  paid: { bg: 'rgba(5, 150, 105, 0.1)', color: '#059669', label: 'Paid' },
  approved: { bg: 'rgba(5, 150, 105, 0.1)', color: '#059669', label: 'Approved' },

  pending: { bg: 'rgba(217, 119, 6, 0.1)', color: '#D97706', label: 'Pending' },
  for_review: { bg: 'rgba(217, 119, 6, 0.1)', color: '#D97706', label: 'For Review' },
  for_correction: { bg: 'rgba(217, 119, 6, 0.1)', color: '#D97706', label: 'For Correction' },
  draft: { bg: 'rgba(100, 116, 139, 0.1)', color: '#64748B', label: 'Draft' },
  unpaid: { bg: 'rgba(217, 119, 6, 0.1)', color: '#D97706', label: 'Unpaid' },

  failed: { bg: 'rgba(220, 38, 38, 0.1)', color: '#DC2626', label: 'Failed' },
  rejected: { bg: 'rgba(220, 38, 38, 0.1)', color: '#DC2626', label: 'Rejected' },
  void: { bg: 'rgba(220, 38, 38, 0.1)', color: '#DC2626', label: 'Void' },
  late: { bg: 'rgba(220, 38, 38, 0.1)', color: '#DC2626', label: 'Late' },
  disputed: { bg: 'rgba(220, 38, 38, 0.1)', color: '#DC2626', label: 'Disputed' },

  transit: { bg: 'rgba(2, 132, 199, 0.1)', color: '#0284C7', label: 'In Transit' },
  sent: { bg: 'rgba(2, 132, 199, 0.1)', color: '#0284C7', label: 'Sent' },
  received: { bg: 'rgba(2, 132, 199, 0.1)', color: '#0284C7', label: 'Received' },
  on_hold: { bg: 'rgba(217, 119, 6, 0.1)', color: '#D97706', label: 'On Hold' },
  ctc: { bg: 'rgba(79, 70, 229, 0.1)', color: '#4F46E5', label: 'CTC' },
  new: { bg: 'rgba(79, 70, 229, 0.1)', color: '#4F46E5', label: 'New' },

  // User Roles (10% Desaturated Opacity Rule)
  financial_manager: { bg: 'rgba(0, 169, 157, 0.1)', color: '#00A99D', label: 'Financial Manager' },
  head_accountant: { bg: 'rgba(2, 132, 199, 0.1)', color: '#0284C7', label: 'Head Accountant' },
  accountant: { bg: 'rgba(79, 70, 229, 0.1)', color: '#4F46E5', label: 'Accountant' },
  assistant_fm: { bg: 'rgba(217, 119, 6, 0.1)', color: '#D97706', label: 'Asst. Financial Manager' },
  client: { bg: 'rgba(100, 116, 139, 0.1)', color: '#64748B', label: 'Client' },
  admin: { bg: 'rgba(27, 37, 75, 0.1)', color: '#1B254B', label: 'Admin' },
};

export const StatusBadge: React.FC<StatusBadgeProps> = ({
  status,
  variant,
  role,
  icon,
  className = '',
}) => {
  const normalizedKey = (role || variant || status || '')
    .toString()
    .toLowerCase()
    .replace(/[^a-z0-9]/g, '_');

  const resolved = STATUS_MAP[normalizedKey] || {
    bg: 'rgba(100, 116, 139, 0.1)',
    color: '#64748B',
    label: status,
  };

  return (
    <span
      className={`badge ${className}`}
      style={{
        backgroundColor: resolved.bg,
        color: resolved.color,
        border: '1px solid transparent',
      }}
      data-testid="status-badge"
    >
      {icon && <span style={{ marginRight: 4, display: 'inline-flex' }}>{icon}</span>}
      {resolved.label}
    </span>
  );
};

export default StatusBadge;
