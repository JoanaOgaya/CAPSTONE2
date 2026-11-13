import React, { useState, useMemo, useEffect } from 'react';
import { useParams, useNavigate, useSearchParams } from 'react-router-dom';
import { DataTable } from '../components/DataTable';
import { StatusBadge } from '../components/shared/StatusBadge';
import { Button } from '../components/Buttons';
import { Card } from '../components/Card';
import { InvoiceDocument } from '../components/InvoiceDocument';
import { InvoiceModal } from '../components/InvoiceModal';
import { InvoiceCreation } from './InvoiceCreation';
import { useQuery } from '../hooks/useQuery';
import { invoicesService } from '../services/invoicesService';
import { clientsService } from '../services/clientsService';
import { paymentsService } from '../services/paymentsService';
import { waybillsService } from '../services/waybillsService';
import { auditLogsService } from '../services/auditLogsService';
import type { Invoice, Client, Payment, Waybill } from '../types/entities';
import { useAuth } from '../context/AuthContext';
import { TableContainer } from '../components/TableContainer';
import { useToast } from '../components/ToastContext';
import { ClientInfoCard } from '../components/ClientInfoCard';
import { CalendarPicker } from '../components/FormModals';
import { formatCurrency, formatDate, formatBillingPeriod } from '../utils/format';

/** Derive payment status from invoice */
function derivePaymentStatus(inv: Invoice): 'Unpaid' | 'Due Soon' | 'Overdue' | 'Paid' {
  if (inv.paymentStatus === 'Paid' || (inv as any).payment_status === 'Paid') return 'Paid';
  const now = Date.now();
  const due = new Date(inv.dueDate).getTime();
  const daysUntilDue = Math.ceil((due - now) / (1000 * 60 * 60 * 24));
  if (daysUntilDue < 0) return 'Overdue';
  if (daysUntilDue <= 7) return 'Due Soon';
  return 'Unpaid';
}

/** Single status-to-actions map defining allowed actions per invoice status */
const STATUS_ACTIONS_MAP: Record<string, string[]> = {
  'Draft': ['View', 'Edit Draft', 'Submit for Approval', 'Send to STARS for Integration'],
  'Pending HA Verification': ['View', 'Approve', 'Return for Revision'],
  'Pending FM Approval': ['View', 'Approve', 'Return for Revision'],
  'Pending Approval': ['View', 'Approve', 'Return for Revision'],
  'For Review': ['View', 'Approve', 'Return for Revision'],
  'Returned to Creator': ['View', 'Edit', 'Resubmit'],
  'Returned to HA': ['View'],
  'Approved': ['View', 'Print', 'Dispatch', 'Send to STARS for Integration'],
  'Sent': ['View', 'Print', 'Mark Received'],
  'Received': ['View', 'Print'],
  'Disputed': ['View', 'Print'],
  'Void': ['View'],
  'Needs Revision': ['View', 'Edit', 'Resubmit', 'Send to STARS for Integration'],
};

export const InvoicingDesk: React.FC = () => {
  const navigate = useNavigate();
  const { id } = useParams();
  const [searchParams] = useSearchParams();

  const { toast } = useToast();
  const { user } = useAuth();

  const userRoleStr = String(user?.role || '');
  const isFinanceManager = userRoleStr.toUpperCase().includes('FINANCE_MANAGER') || userRoleStr.toLowerCase().includes('finance manager');
  const isAccountant = userRoleStr.toUpperCase().includes('ACCOUNTANT') || (!isFinanceManager && !userRoleStr.toUpperCase().includes('HEAD_ACCOUNTANT') && !userRoleStr.toUpperCase().includes('ASSISTANT_FM'));

  const { data: invData, refetch: refetchInvoices } = useQuery('desk:invoices', () => invoicesService.list({ pageSize: 100 }));
  const { data: clientData } = useQuery('desk:clients', () => clientsService.list({ pageSize: 100 }));
  const { data: payData } = useQuery('desk:payments', () => paymentsService.list({ pageSize: 100 }));
  const { data: waybillData } = useQuery('desk:waybills', () => waybillsService.list({ pageSize: 100 }));

  // Finance Manager must not see draft invoices (SOD / creator workflow boundary)
  const invoices: Invoice[] = useMemo(() => {
    return (invData?.items || []).filter((inv: Invoice) => {
      if (inv.invoiceNumber?.startsWith('BI-')) return false;
      const statusStr = String(inv.status || '');
      if (isFinanceManager && (statusStr === 'Draft' || statusStr === 'Listed')) return false;
      return true;
    });
  }, [invData?.items, isFinanceManager]);
  const clients: Client[] = clientData?.items || [];
  const payments: Payment[] = payData?.items || [];
  const allWaybills: Waybill[] = waybillData?.items || [];

  // Modal state for viewing invoice without navigating away
  const [modalInvoiceId, setModalInvoiceId] = useState<string | null>(null);
  const [modalInvoice, setModalInvoice] = useState<Invoice | null>(null);
  const [isAutoPrint, setIsAutoPrint] = useState<boolean>(false);

  // Preserve legacy direct URL route /invoicing-desk/:id if navigated to directly
  let viewInvoice: Invoice | undefined = undefined;
  let viewClient: Client | undefined = undefined;

  if (id) {
    viewInvoice = invoices.find(i => i.id === id);
    if (!viewInvoice) {
      viewClient = clients.find(c => c.id === id);
    }
  }

  const selectedClientId = viewClient ? viewClient.id : null;

  type InvoiceTab = 'All' | 'Pending' | 'Listed' | 'Approved' | 'Needs Revision';
  const initialTabParam = searchParams.get('tab') || searchParams.get('status') || 'All';
  const initialNormalizedTab: InvoiceTab =
    initialTabParam.toLowerCase().includes('pending') ? 'Pending' :
    (!isFinanceManager && (initialTabParam.toLowerCase().includes('draft') || initialTabParam.toLowerCase().includes('listed'))) ? 'Listed' :
    initialTabParam.toLowerCase().includes('approved') ? 'Approved' :
    initialTabParam.toLowerCase().includes('revision') ? 'Needs Revision' : 'All';

  const [activeTab, setActiveTab] = useState<InvoiceTab>(initialNormalizedTab);

  useEffect(() => {
    if (isFinanceManager && activeTab === 'Listed') {
      setActiveTab('All');
    }
  }, [isFinanceManager, activeTab]);
  const [filterDateFrom, setFilterDateFrom] = useState('');
  const [filterDateTo, setFilterDateTo] = useState('');
  const [viewMode, setViewMode] = useState<'cards' | 'table'>('cards');
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedStatus, setSelectedStatus] = useState('All');
  const [selectedClientName, setSelectedClientName] = useState('All');
  const [transitioningId, setTransitioningId] = useState<string | null>(null);
  const [sendingToStarsId, setSendingToStarsId] = useState<string | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [editingInvoiceId, setEditingInvoiceId] = useState<string | null>(null);

  // Tab counts for quick lifecycle overview
  const tabCounts = useMemo(() => {
    return {
      all: invoices.length,
      pending: invoices.filter(i => ['Pending HA Verification', 'Pending FM Approval', 'Pending Approval', 'Pending', 'For Review'].includes(i.status as string)).length,
      listed: invoices.filter(i => ['Draft', 'Listed'].includes(i.status as string)).length,
      approved: invoices.filter(i => ['Approved', 'Sent', 'Received', 'Endorsed', 'Validated'].includes(i.status as string)).length,
      needsRevision: invoices.filter(i => ['Returned to Creator', 'Returned to HA', 'Needs Revision', 'Returned for revision'].includes(i.status as string)).length,
      voided: invoices.filter(i => (i.status as string) === 'Void').length,
    };
  }, [invoices]);

  // Enrich invoices with client names, proper waybill counts, and derived status
  const enrichedInvoices = useMemo(() => {
    const base = selectedClientId
      ? invoices.filter(i => i.clientId === selectedClientId)
      : invoices;

    const activeInvoices = base.filter((inv: Invoice) => {
      if (!['Draft', 'Needs Revision', 'Pending Approval', 'For Review', 'Pending HA Verification', 'Pending FM Approval', 'Pending', 'Approved', 'Sent', 'Received', 'Endorsed', 'Validated'].includes(inv.status)) return false;
      if (filterDateFrom || filterDateTo) {
        const itemDate = new Date(inv.createdAt);
        itemDate.setHours(0, 0, 0, 0);
        if (filterDateFrom) {
          const fromDate = new Date(filterDateFrom);
          fromDate.setHours(0, 0, 0, 0);
          if (itemDate < fromDate) return false;
        }
        if (filterDateTo) {
          const toDate = new Date(filterDateTo);
          toDate.setHours(23, 59, 59, 999);
          if (itemDate > toDate) return false;
        }
      }
      return true;
    });

    const enriched = activeInvoices.map((inv: Invoice) => {
      const client = clients.find(c => c.id === inv.clientId);
      const ps = derivePaymentStatus(inv);

      // Match linked waybills by waybillIds or by matching invoiceId
      const linked = allWaybills.filter(w =>
        (inv.waybillIds && (inv.waybillIds.includes(String(w.id)) || inv.waybillIds.includes(w.waybillNumber))) ||
        (w.invoiceId && (String(w.invoiceId) === String(inv.id) || w.invoiceId === inv.invoiceNumber))
      );
      const calculatedCount = inv.waybillIds && inv.waybillIds.length > 0 ? inv.waybillIds.length : linked.length;

      return {
        ...inv,
        clientName: client?.name ?? (inv as any).clientName ?? inv.clientId,
        waybillCount: calculatedCount,
        paymentStatus: ps,
      };
    });

    return enriched.filter(inv => {
      if (searchQuery) {
        const q = searchQuery.toLowerCase();
        if (!inv.invoiceNumber.toLowerCase().includes(q) && !inv.clientName.toLowerCase().includes(q)) {
          return false;
        }
      }
      // Explicit Tab Lifecycle Filtering
      const invStatus = inv.status as string;
      if (activeTab === 'Pending') {
        if (!['Pending HA Verification', 'Pending FM Approval', 'Pending Approval', 'Pending', 'For Review'].includes(invStatus)) return false;
      } else if (activeTab === 'Listed') {
        if (invStatus !== 'Draft' && invStatus !== 'Listed') return false;
      } else if (activeTab === 'Approved') {
        if (!['Approved', 'Sent', 'Received', 'Endorsed', 'Validated'].includes(invStatus)) return false;
      } else if (activeTab === 'Needs Revision') {
        if (!['Returned to Creator', 'Returned to HA', 'Needs Revision', 'Returned for revision'].includes(invStatus)) return false;
      } else if (selectedStatus !== 'All' && inv.status !== selectedStatus) {
        return false;
      }

      if (selectedClientName !== 'All' && inv.clientName !== selectedClientName) {
        return false;
      }
      return true;
    });
  }, [selectedClientId, invoices, filterDateFrom, filterDateTo, clients, allWaybills, searchQuery, activeTab, selectedStatus, selectedClientName]);

  const allClients: string[] = useMemo(() => {
    return Array.from(new Set(invoices.map((inv: Invoice) => {
      const client = clients.find(c => c.id === inv.clientId);
      return client?.name ?? (inv as any).clientName ?? inv.clientId;
    }))).filter(Boolean);
  }, [invoices, clients]);

  const handleTransition = async (
    invId: string,
    actionOrStatus: string,
    successMsg: string,
    payload?: any
  ) => {
    const inv = invoices.find((i) => i.id === invId);
    if (!inv) return;

    setTransitioningId(invId);

    let action = 'submit';
    const lower = (actionOrStatus || '').toLowerCase();
    if (lower.includes('submit') || lower.includes('pending ha') || lower.includes('pending approval') || lower.includes('for review') || lower.includes('verification')) {
      action = (inv.status === 'Needs Revision' || inv.status === 'Returned to Creator') ? 'resubmit' : 'submit';
    } else if (lower.includes('approve')) {
      action = 'approve';
    } else if (lower.includes('verify')) {
      action = 'verify';
    } else if (lower.includes('dispatch') || lower.includes('send') || lower.includes('sent')) {
      action = 'send';
    } else if (lower.includes('receive') || lower.includes('received')) {
      action = 'receive';
    } else if (lower.includes('return') || lower.includes('revision') || lower.includes('reject')) {
      action = 'return';
    } else if (lower.includes('void')) {
      action = 'void';
    }

    try {
      await invoicesService.act(invId, action as any, payload || {});
      await auditLogsService.create({
        action: 'INVOICE_STATUS_CHANGE',
        module: 'InvoicingDesk',
        recordId: invId,
        recordType: 'Invoice',
        details: `Action: ${action} on invoice ${inv.invoiceNumber}`,
        ipAddress: '127.0.0.1',
      }).catch(() => {});
      await refetchInvoices();
      toast.success(successMsg, 'Success');
    } catch (err: any) {
      const errMsg = err?.response?.data?.message || err?.message || 'Failed to update invoice status.';
      toast.error(errMsg, 'Error');
    } finally {
      setTransitioningId(null);
    }
  };

  const handleSendToStars = async (inv: Invoice) => {
    setSendingToStarsId(inv.id);
    try {
      await invoicesService.sendToStars(inv.invoiceNumber);
      await auditLogsService.create({
        action: 'SEND_TO_STARS',
        module: 'InvoicingDesk',
        recordId: inv.id,
        recordType: 'Invoice',
        details: `Dispatched invoice ${inv.invoiceNumber} to STARS integration`,
        ipAddress: '127.0.0.1',
      }).catch(() => {});
      toast.success(`Invoice ${inv.invoiceNumber} successfully sent to STARS for integration endorsement.`, 'STARS Integration');
      refetchInvoices();
    } catch (err: any) {
      const errMsg = err?.response?.data?.message || err?.message || 'Failed to dispatch invoice to STARS.';
      toast.error(errMsg, 'STARS Error');
    } finally {
      setSendingToStarsId(null);
    }
  };

  const tableColumns = [
    { key: 'invoiceNumber', label: 'Invoice No.', sortable: true },
    { key: 'clientName', label: 'Client', sortable: true },
    {
      key: 'billingPeriod',
      label: 'Billing Period',
      render: (row: any) => (
        <span style={{ whiteSpace: 'nowrap' }}>
          {formatBillingPeriod(row.billingPeriod)}
        </span>
      ),
    },
    {
      key: 'waybillCount',
      label: 'Waybills',
      render: (row: any) => `${row.waybillCount} waybills`,
    },
    { key: 'createdAt', label: 'Date', render: (row: any) => formatDate(row.createdAt) },
    { key: 'totalAmount', label: 'Total Amount', render: (row: any) => formatCurrency(row.totalAmount) },
    { key: 'status', label: 'Invoice Status', render: (row: any) => <StatusBadge status={row.status} /> },
  ];

  // Action configurations mapping action names to handlers and icons
  const ACTION_CONFIGS: Record<string, { label: string; icon: string; onClick: (row: any) => void }> = {
    'Submit for Verification': {
      label: 'Submit for Verification',
      icon: 'ti-send',
      onClick: (row: any) => handleTransition(row.id, 'Pending HA Verification' as any, `Invoice ${row.invoiceNumber} submitted for verification`),
    },
    'Print': {
      label: 'Print',
      icon: 'ti-printer',
      onClick: (row: any) => {
        setModalInvoice(row);
        setModalInvoiceId(row.id);
        setIsAutoPrint(true);
      },
    },
    'Dispatch': {
      label: 'Dispatch',
      icon: 'ti-truck-delivery',
      onClick: (row: any) => handleTransition(row.id, 'Sent' as any, `Invoice ${row.invoiceNumber} dispatched`),
    },
    'Mark Received': {
      label: 'Mark Received',
      icon: 'ti-check-double',
      onClick: (row: any) => handleTransition(row.id, 'Received' as any, `Invoice ${row.invoiceNumber} marked as received`),
    },
    'View': {
      label: 'View',
      icon: 'ti-eye',
      onClick: (row: any) => {
        setModalInvoice(row);
        setModalInvoiceId(row.id);
        setIsAutoPrint(false);
      },
    },
    'Edit Draft': {
      label: 'Edit Draft',
      icon: 'ti-pencil',
      onClick: (row: any) => {
        setEditingInvoiceId(row.id);
        setShowCreateModal(true);
      },
    },
    'Edit': {
      label: 'Edit',
      icon: 'ti-pencil',
      onClick: (row: any) => navigate(`/invoice-create?edit=${row.id}`),
    },
    'Submit for Approval': {
      label: 'Submit for Approval',
      icon: 'ti-send',
      onClick: (row: any) => handleTransition(row.id, 'Pending Approval', `Invoice ${row.invoiceNumber} submitted for approval`),
    },
    'Approve': {
      label: 'Approve',
      icon: 'ti-check',
      onClick: (row: any) => handleTransition(row.id, 'Approved', `Invoice ${row.invoiceNumber} approved`),
    },
    'Return for Revision': {
      label: 'Return for Revision',
      icon: 'ti-rotate-2',
      onClick: (row: any) => handleTransition(row.id, 'Needs Revision', `Invoice ${row.invoiceNumber} returned for revision`),
    },
    'Resubmit': {
      label: 'Resubmit',
      icon: 'ti-send',
      onClick: (row: any) => handleTransition(row.id, 'Pending Approval', `Invoice ${row.invoiceNumber} resubmitted for approval`),
    },
    'Download PDF': {
      label: 'Download PDF',
      icon: 'ti-file-type-pdf',
      onClick: (row: any) => {
        setModalInvoice(row);
        setModalInvoiceId(row.id);
        setIsAutoPrint(true);
      },
    },
    'Send to STARS for Integration': {
      label: 'Send to STARS for Integration',
      icon: 'ti-cloud-upload',
      onClick: (row: any) => handleSendToStars(row),
    },
  };

  const actions = Object.entries(ACTION_CONFIGS).map(([key, item]) => ({
    label: item.label,
    icon: item.icon,
    onClick: item.onClick,
    hidden: (row: any) => !(STATUS_ACTIONS_MAP[row.status] || ['View']).includes(key),
  }));

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
      {selectedClientId && viewClient ? (
        <ClientInfoCard client={viewClient} onBack={() => navigate('/invoicing-desk')} />
      ) : null}

      {viewInvoice ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Button title="Back to Invoices" variant="secondary" icon="ti-arrow-left" onClick={() => navigate('/invoicing-desk')} />
            <div style={{ display: 'flex', gap: 8 }}>
              {(viewInvoice.status === 'Draft' || viewInvoice.status === 'Needs Revision') && (
                <Button title="Edit Invoice" variant="secondary" icon="ti-pencil" onClick={() => navigate(`/invoice-create?edit=${viewInvoice?.id}`)} />
              )}
              {viewInvoice.status === 'Approved' && (
                <>
                  <Button title="Send to STARS for Integration" variant="primary" icon="ti-cloud-upload" onClick={() => handleSendToStars(viewInvoice)} />
                  <Button title="Mark as Sent" variant="secondary" icon="ti-send" onClick={() => handleTransition(viewInvoice!.id, 'Sent', 'Invoice marked as sent')} />
                </>
              )}
              {viewInvoice.status === 'Pending Approval' && (
                <>
                  <Button title="Approve Invoice" variant="primary" icon="ti-check" onClick={() => handleTransition(viewInvoice!.id, 'Approved', `Invoice ${viewInvoice!.invoiceNumber} approved`)} />
                  <Button title="Return for Revision" variant="danger" icon="ti-rotate-2" onClick={() => handleTransition(viewInvoice!.id, 'Needs Revision', `Invoice ${viewInvoice!.invoiceNumber} returned for revision`)} />
                </>
              )}
            </div>
          </div>
          <Card>
            <InvoiceDocument invoice={viewInvoice} />
          </Card>
        </div>
      ) : (
        <div style={{ background: '#FFFFFF', borderRadius: '16px', border: '1px solid #E2E8F0', padding: '24px', display: 'flex', flexDirection: 'column', gap: '20px', boxShadow: '0 1px 3px rgba(0,0,0,0.02)' }}>
        
        {/* Status Lifecycle Filter Tabs */}
        <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', borderBottom: '1px solid #E2E8F0', paddingBottom: '14px' }}>
          {[
            { key: 'All', label: 'All Invoices', count: tabCounts.all, icon: 'ti-files' },
            { key: 'Pending', label: 'Pending', count: tabCounts.pending, icon: 'ti-clock' },
            ...(!isFinanceManager ? [{ key: 'Listed', label: 'Listed', count: tabCounts.listed, icon: 'ti-list-details' }] : []),
            { key: 'Approved', label: 'Approved', count: tabCounts.approved, icon: 'ti-check' },
            ...(tabCounts.needsRevision > 0 ? [{ key: 'Needs Revision', label: 'Needs Revision', count: tabCounts.needsRevision, icon: 'ti-alert-circle' }] : []),
          ].map((tab) => {
            const isActive = activeTab === tab.key;
            return (
              <button
                key={tab.key}
                type="button"
                onClick={() => {
                  setActiveTab(tab.key as any);
                  setSelectedStatus('All');
                }}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '8px',
                  padding: '8px 16px',
                  borderRadius: '9999px',
                  fontSize: '0.85rem',
                  fontWeight: 700,
                  border: isActive ? '1px solid #0D9488' : '1px solid #E2E8F0',
                  background: isActive ? '#0D9488' : '#FFFFFF',
                  color: isActive ? '#FFFFFF' : '#475569',
                  cursor: 'pointer',
                  transition: 'all 0.15s ease',
                  boxShadow: isActive ? '0 2px 4px rgba(13, 148, 136, 0.2)' : 'none',
                }}
              >
                <i className={`ti ${tab.icon}`} />
                <span>{tab.label}</span>
                <span
                  style={{
                    fontSize: '0.75rem',
                    padding: '2px 7px',
                    borderRadius: '9999px',
                    background: isActive ? 'rgba(255, 255, 255, 0.25)' : '#F1F5F9',
                    color: isActive ? '#FFFFFF' : '#64748B',
                    fontWeight: 800,
                  }}
                >
                  {tab.count}
                </span>
              </button>
            );
          })}
        </div>

        {/* Header Row: Title & View Mode Toggle */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '16px' }}>
          <div>
            <p style={{ margin: 0, fontSize: '0.85rem', color: '#64748B' }}>Client sales invoices, status tracking, and dispatch records</p>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            {user?.role === 'ACCOUNTANT' && (
              <button
                type="button"
                onClick={() => {
                  setEditingInvoiceId(null);
                  setShowCreateModal(true);
                }}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '6px',
                  padding: '8px 16px',
                  background: '#0D9488',
                  color: '#FFFFFF',
                  borderRadius: '8px',
                  fontSize: '0.85rem',
                  fontWeight: 700,
                  border: 'none',
                  cursor: 'pointer',
                  boxShadow: '0 2px 4px rgba(13, 148, 136, 0.25)',
                  transition: 'all 0.15s ease',
                }}
                onMouseEnter={(e) => (e.currentTarget.style.background = '#0F766E')}
                onMouseLeave={(e) => (e.currentTarget.style.background = '#0D9488')}
              >
                <i className="ti ti-plus" style={{ fontSize: '15px' }} />
                <span>Create Invoice</span>
              </button>
            )}

            {/* Cards vs Table View Switcher */}
          <div style={{ display: 'inline-flex', borderRadius: '8px', border: '1px solid #E2E8F0', overflow: 'hidden', background: '#F8FAFC' }}>
            <button
              type="button"
              onClick={() => setViewMode('cards')}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
                padding: '8px 16px',
                fontSize: '0.85rem',
                fontWeight: 600,
                border: 'none',
                cursor: 'pointer',
                background: viewMode === 'cards' ? '#0D9488' : 'transparent',
                color: viewMode === 'cards' ? '#FFFFFF' : '#64748B',
                transition: 'all 0.2s ease',
              }}
            >
              <i className="ti ti-layout-grid" style={{ fontSize: '15px' }} />
              Cards
            </button>
            <button
              type="button"
              onClick={() => setViewMode('table')}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
                padding: '8px 16px',
                fontSize: '0.85rem',
                fontWeight: 600,
                border: 'none',
                cursor: 'pointer',
                background: viewMode === 'table' ? '#0D9488' : 'transparent',
                color: viewMode === 'table' ? '#FFFFFF' : '#64748B',
                transition: 'all 0.2s ease',
              }}
            >
              <i className="ti ti-table" style={{ fontSize: '15px' }} />
              Table
            </button>
          </div>
          </div>
        </div>

        {/* Filters Row */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
            <div style={{ position: 'relative', width: '300px' }}>
              <i className="ti ti-search" style={{ position: 'absolute', left: '14px', top: '50%', transform: 'translateY(-50%)', color: '#0D9488', fontSize: '16px' }} />
              <input
                type="text"
                placeholder="Search by invoice number or client..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                style={{
                  width: '100%',
                  padding: '9px 16px 9px 40px',
                  borderRadius: '9999px',
                  border: '1.5px solid #0D9488',
                  background: '#FFFFFF',
                  fontSize: '0.85rem',
                  color: '#0F172A',
                  outline: 'none',
                  boxSizing: 'border-box',
                }}
              />
            </div>

            <select
              value={selectedClientName}
              onChange={(e) => setSelectedClientName(e.target.value)}
              style={{
                padding: '9px 14px',
                borderRadius: '8px',
                border: '1px solid #E2E8F0',
                background: '#FFFFFF',
                fontSize: '0.85rem',
                color: '#334155',
                fontWeight: 600,
                cursor: 'pointer',
                outline: 'none',
              }}
            >
              <option value="All">All Clients</option>
              {allClients.map(c => (
                <option key={c} value={c}>{c}</option>
              ))}
            </select>

            <select
              value={selectedStatus}
              onChange={(e) => setSelectedStatus(e.target.value)}
              style={{
                padding: '9px 14px',
                borderRadius: '8px',
                border: '1px solid #E2E8F0',
                background: '#FFFFFF',
                fontSize: '0.85rem',
                color: '#334155',
                fontWeight: 600,
                cursor: 'pointer',
                outline: 'none',
              }}
            >
              <option value="All">All Statuses</option>
              {!isFinanceManager && <option value="Draft">Draft</option>}
              <option value="Pending Approval">Pending Approval</option>
              <option value="Approved">Approved</option>
              <option value="Needs Revision">Needs Revision</option>
            </select>
          </div>

          {/* Date Row Below Search */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
            <CalendarPicker
              label="From:"
              placeholder="Start date..."
              value={filterDateFrom}
              onChange={date => {
                if (filterDateTo && date > filterDateTo) {
                  toast.error('Start date cannot be later than end date.');
                  return;
                }
                setFilterDateFrom(date);
              }}
              maxDate={filterDateTo}
              variant="toolbar"
            />

            <CalendarPicker
              label="To:"
              placeholder="End date..."
              value={filterDateTo}
              onChange={date => {
                if (filterDateFrom && date < filterDateFrom) {
                  toast.error('End date cannot be earlier than start date.');
                  return;
                }
                setFilterDateTo(date);
              }}
              minDate={filterDateFrom}
              variant="toolbar"
            />

            {(searchQuery || selectedClientName !== 'All' || selectedStatus !== 'All' || filterDateFrom || filterDateTo) && (
              <button
                type="button"
                onClick={() => {
                  setSearchQuery('');
                  setSelectedClientName('All');
                  setSelectedStatus('All');
                  setFilterDateFrom('');
                  setFilterDateTo('');
                }}
                style={{
                  padding: '8px 14px',
                  borderRadius: '8px',
                  border: '1px solid #FCA5A5',
                  background: '#FEF2F2',
                  color: '#DC2626',
                  fontWeight: 600,
                  fontSize: '0.82rem',
                  cursor: 'pointer',
                  display: 'inline-flex',
                  alignItems: 'center',
                }}
              >
                <i className="ti ti-x" style={{ marginRight: 4 }} />Clear Filters
              </button>
            )}
          </div>
        </div>

        {/* View Mode: Cards or Table */}
        {viewMode === 'cards' ? (
          enrichedInvoices.length === 0 ? (
            <div style={{ textAlign: 'center', padding: '64px 20px', color: '#94A3B8' }}>
              <i className="ti ti-file-invoice" style={{ fontSize: '42px', display: 'block', marginBottom: '12px', color: '#CBD5E1' }} />
              <div style={{ fontSize: '1rem', fontWeight: 700, color: '#334155' }}>No invoices found</div>
              <p style={{ margin: '4px 0 0', fontSize: '0.85rem', color: '#64748B' }}>Try adjusting your search query or filter options.</p>
            </div>
          ) : (
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(330px, 1fr))', gap: '20px' }}>
              {enrichedInvoices.map((inv: any) => (
                <div
                  key={inv.id}
                  style={{
                    background: '#FFFFFF',
                    border: '1px solid #E2E8F0',
                    borderRadius: '16px',
                    padding: '20px',
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'space-between',
                    gap: '16px',
                    boxShadow: '0 2px 4px rgba(0, 0, 0, 0.02)',
                    transition: 'all 0.2s ease',
                  }}
                  onMouseEnter={(e) => {
                    e.currentTarget.style.boxShadow = '0 12px 24px -6px rgba(0, 0, 0, 0.08), 0 4px 8px -4px rgba(0, 0, 0, 0.04)';
                    e.currentTarget.style.transform = 'translateY(-2px)';
                  }}
                  onMouseLeave={(e) => {
                    e.currentTarget.style.boxShadow = '0 2px 4px rgba(0, 0, 0, 0.02)';
                    e.currentTarget.style.transform = 'translateY(0)';
                  }}
                >
                  {/* Top: Invoice No & Status Badge */}
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={{ fontFamily: 'monospace', fontWeight: 800, color: '#0F172A', fontSize: '0.9rem' }}>
                      {inv.invoiceNumber}
                    </span>
                    <StatusBadge status={inv.status} />
                  </div>

                  {/* Client Info */}
                  <div>
                    <h3 style={{ margin: 0, fontSize: '1.05rem', fontWeight: 800, color: '#0F172A', lineHeight: 1.3 }}>
                      {inv.clientName}
                    </h3>
                    <div style={{ display: 'flex', gap: '16px', marginTop: '6px', fontSize: '0.75rem', color: '#64748B' }}>
                      <span><i className="ti ti-calendar" style={{ marginRight: 4 }} />Due: <strong>{formatDate(inv.dueDate)}</strong></span>
                      <span><i className="ti ti-truck-delivery" style={{ marginRight: 4 }} /><strong>{inv.waybillCount}</strong> Waybills</span>
                    </div>
                  </div>

                  {/* Total Amount Block */}
                  <div style={{ background: '#F8FAFC', borderRadius: '12px', padding: '12px 14px', border: '1px solid #F1F5F9' }}>
                    <div style={{ fontSize: '0.7rem', fontWeight: 700, color: '#64748B', letterSpacing: '0.04em' }}>TOTAL INVOICE AMOUNT</div>
                    <div style={{ fontSize: '1.25rem', fontWeight: 800, color: '#0F172A', marginTop: '2px' }}>
                      ₱{Number(inv.totalAmount || inv.amount || 0).toLocaleString('en-PH', { minimumFractionDigits: 2 })}
                    </div>
                  </div>

                  {/* Footer Actions */}
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderTop: '1px solid #F1F5F9', paddingTop: '12px' }}>
                    <button
                      type="button"
                      onClick={() => {
                        setModalInvoice(inv);
                        setModalInvoiceId(inv.id);
                        setIsAutoPrint(false);
                      }}
                      style={{
                        display: 'inline-flex',
                        alignItems: 'center',
                        gap: '6px',
                        padding: '8px 14px',
                        background: '#0D9488',
                        color: '#FFFFFF',
                        border: 'none',
                        borderRadius: '8px',
                        fontSize: '0.82rem',
                        fontWeight: 700,
                        cursor: 'pointer',
                        transition: 'background 0.2s',
                      }}
                      onMouseEnter={(e) => (e.currentTarget.style.background = '#0F766E')}
                      onMouseLeave={(e) => (e.currentTarget.style.background = '#0D9488')}
                    >
                      <i className="ti ti-eye" />
                      View Invoice
                    </button>

                    <div style={{ display: 'flex', gap: '6px' }}>
                      {(inv.status === 'Draft' || inv.status === 'Needs Revision') && isAccountant && (
                        <>
                          <button
                            type="button"
                            onClick={() => { setEditingInvoiceId(inv.id); setShowCreateModal(true); }}
                            style={{
                              padding: '8px 10px',
                              background: '#F1F5F9',
                              color: '#334155',
                              border: '1px solid #E2E8F0',
                              borderRadius: '8px',
                              fontSize: '0.8rem',
                              fontWeight: 600,
                              cursor: 'pointer',
                              transition: 'all 0.15s ease',
                            }}
                            onMouseEnter={(e) => {
                              e.currentTarget.style.background = '#E2E8F0';
                              e.currentTarget.style.transform = 'translateY(-1px)';
                            }}
                            onMouseLeave={(e) => {
                              e.currentTarget.style.background = '#F1F5F9';
                              e.currentTarget.style.transform = 'translateY(0)';
                            }}
                            title="Edit Invoice"
                          >
                            <i className="ti ti-pencil" />
                          </button>

                          <button
                            type="button"
                            disabled={transitioningId === inv.id}
                            onClick={() => handleTransition(inv.id, 'Pending Approval', `Invoice ${inv.invoiceNumber} submitted for approval`)}
                            style={{
                              display: 'inline-flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                              padding: '8px 11px',
                              background: transitioningId === inv.id ? '#99F6E4' : '#F0FDFA',
                              color: '#0D9488',
                              border: '1px solid #99F6E4',
                              borderRadius: '8px',
                              fontSize: '0.85rem',
                              fontWeight: 700,
                              cursor: transitioningId === inv.id ? 'not-allowed' : 'pointer',
                              transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
                              boxShadow: '0 1px 2px rgba(13, 148, 136, 0.08)',
                            }}
                            onMouseEnter={(e) => {
                              if (transitioningId !== inv.id) {
                                e.currentTarget.style.background = '#0D9488';
                                e.currentTarget.style.color = '#FFFFFF';
                                e.currentTarget.style.borderColor = '#0D9488';
                                e.currentTarget.style.boxShadow = '0 4px 12px rgba(13, 148, 136, 0.25)';
                                e.currentTarget.style.transform = 'translateY(-1px)';
                              }
                            }}
                            onMouseLeave={(e) => {
                              if (transitioningId !== inv.id) {
                                e.currentTarget.style.background = '#F0FDFA';
                                e.currentTarget.style.color = '#0D9488';
                                e.currentTarget.style.borderColor = '#99F6E4';
                                e.currentTarget.style.boxShadow = '0 1px 2px rgba(13, 148, 136, 0.08)';
                                e.currentTarget.style.transform = 'translateY(0)';
                              }
                            }}
                            title="Submit for Approval"
                          >
                            {transitioningId === inv.id ? (
                              <i className="ti ti-loader-2 btn-spinner" />
                            ) : (
                              <i className="ti ti-cloud-upload" />
                            )}
                          </button>
                        </>
                      )}

                      {(inv.status === 'Pending Approval' || inv.status === 'Pending HA Verification' || inv.status === 'For Review' || inv.status === 'Pending') && (
                        <>
                          <button
                            type="button"
                            disabled={transitioningId === inv.id}
                            onClick={() => handleTransition(inv.id, 'approve', `Invoice ${inv.invoiceNumber} approved`)}
                            style={{
                              display: 'inline-flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                              padding: '8px 11px',
                              background: '#ECFDF5',
                              color: '#059669',
                              border: '1px solid #A7F3D0',
                              borderRadius: '8px',
                              fontSize: '0.85rem',
                              fontWeight: 700,
                              cursor: transitioningId === inv.id ? 'not-allowed' : 'pointer',
                              transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
                              boxShadow: '0 1px 2px rgba(5, 150, 105, 0.08)',
                            }}
                            onMouseEnter={(e) => {
                              if (transitioningId !== inv.id) {
                                e.currentTarget.style.background = '#059669';
                                e.currentTarget.style.color = '#FFFFFF';
                                e.currentTarget.style.borderColor = '#059669';
                                e.currentTarget.style.boxShadow = '0 4px 12px rgba(5, 150, 105, 0.25)';
                                e.currentTarget.style.transform = 'translateY(-1px)';
                              }
                            }}
                            onMouseLeave={(e) => {
                              if (transitioningId !== inv.id) {
                                e.currentTarget.style.background = '#ECFDF5';
                                e.currentTarget.style.color = '#059669';
                                e.currentTarget.style.borderColor = '#A7F3D0';
                                e.currentTarget.style.boxShadow = '0 1px 2px rgba(5, 150, 105, 0.08)';
                                e.currentTarget.style.transform = 'translateY(0)';
                              }
                            }}
                            title="Approve Invoice"
                          >
                            {transitioningId === inv.id ? (
                              <i className="ti ti-loader-2 btn-spinner" />
                            ) : (
                              <i className="ti ti-check" />
                            )}
                          </button>

                          <button
                            type="button"
                            disabled={transitioningId === inv.id}
                            onClick={() => handleTransition(inv.id, 'return', `Invoice ${inv.invoiceNumber} returned for revision`, { remarks: 'Returned for revision' })}
                            style={{
                              display: 'inline-flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                              padding: '8px 11px',
                              background: '#FEF2F2',
                              color: '#DC2626',
                              border: '1px solid #FECACA',
                              borderRadius: '8px',
                              fontSize: '0.85rem',
                              fontWeight: 700,
                              cursor: transitioningId === inv.id ? 'not-allowed' : 'pointer',
                              transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
                              boxShadow: '0 1px 2px rgba(220, 38, 38, 0.08)',
                            }}
                            onMouseEnter={(e) => {
                              if (transitioningId !== inv.id) {
                                e.currentTarget.style.background = '#DC2626';
                                e.currentTarget.style.color = '#FFFFFF';
                                e.currentTarget.style.borderColor = '#DC2626';
                                e.currentTarget.style.boxShadow = '0 4px 12px rgba(220, 38, 38, 0.25)';
                                e.currentTarget.style.transform = 'translateY(-1px)';
                              }
                            }}
                            onMouseLeave={(e) => {
                              if (transitioningId !== inv.id) {
                                e.currentTarget.style.background = '#FEF2F2';
                                e.currentTarget.style.color = '#DC2626';
                                e.currentTarget.style.borderColor = '#FECACA';
                                e.currentTarget.style.boxShadow = '0 1px 2px rgba(220, 38, 38, 0.08)';
                                e.currentTarget.style.transform = 'translateY(0)';
                              }
                            }}
                            title="Return for Revision"
                          >
                            <i className="ti ti-rotate-2" />
                          </button>
                        </>
                      )}

                      {inv.status === 'Approved' && (
                        <>
                          <button
                            type="button"
                            onClick={() => handleSendToStars(inv)}
                            style={{
                              display: 'inline-flex',
                              alignItems: 'center',
                              gap: '6px',
                              padding: '8px 12px',
                              background: '#0D9488',
                              color: '#FFFFFF',
                              border: 'none',
                              borderRadius: '8px',
                              fontSize: '0.8rem',
                              fontWeight: 700,
                              cursor: 'pointer',
                              transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
                              boxShadow: '0 2px 6px rgba(13, 148, 136, 0.2)',
                              whiteSpace: 'nowrap',
                            }}
                            onMouseEnter={(e) => {
                              e.currentTarget.style.background = '#0F766E';
                              e.currentTarget.style.transform = 'translateY(-1px)';
                              e.currentTarget.style.boxShadow = '0 4px 12px rgba(13, 148, 136, 0.35)';
                            }}
                            onMouseLeave={(e) => {
                              e.currentTarget.style.background = '#0D9488';
                              e.currentTarget.style.transform = 'translateY(0)';
                              e.currentTarget.style.boxShadow = '0 2px 6px rgba(13, 148, 136, 0.2)';
                            }}
                            title="Send to STARS for Integration"
                          >
                            <i className="ti ti-cloud-upload" />
                            Send to STARS
                          </button>

                          <button
                            type="button"
                            disabled={transitioningId === inv.id}
                            onClick={() => handleTransition(inv.id, 'send', `Invoice ${inv.invoiceNumber} dispatched`)}
                            style={{
                              display: 'inline-flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                              padding: '8px 11px',
                              background: '#F0FDFA',
                              color: '#0D9488',
                              border: '1px solid #99F6E4',
                              borderRadius: '8px',
                              fontSize: '0.85rem',
                              fontWeight: 700,
                              cursor: transitioningId === inv.id ? 'not-allowed' : 'pointer',
                              transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
                            }}
                            title="Dispatch Invoice"
                          >
                            <i className="ti ti-truck-delivery" />
                          </button>
                        </>
                      )}

                      {inv.status === 'Sent' && (
                        <button
                          type="button"
                          disabled={transitioningId === inv.id}
                          onClick={() => handleTransition(inv.id, 'receive', `Invoice ${inv.invoiceNumber} marked as received`)}
                          style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            padding: '8px 11px',
                            background: '#EFF6FF',
                            color: '#2563EB',
                            border: '1px solid #BFDBFE',
                            borderRadius: '8px',
                            fontSize: '0.85rem',
                            fontWeight: 700,
                            cursor: transitioningId === inv.id ? 'not-allowed' : 'pointer',
                            transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
                          }}
                          title="Mark Received"
                        >
                          <i className="ti ti-check-double" />
                        </button>
                      )}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )
        ) : (
          <TableContainer style={{ border: 'none', boxShadow: 'none', background: 'transparent' }}>
            <DataTable
              data={enrichedInvoices}
              columns={tableColumns}
              actions={actions}
              rowKey="id"
              hideSearch={true}
              columnToggle={true}
              densityToggle={true}
              exportable={false}
              emptyMessage="No invoices found."
            />
          </TableContainer>
        )}
      </div>
      )}

      {/* Create / Edit Invoice Popup Modal */}
      {showCreateModal && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(15, 23, 42, 0.65)',
            backdropFilter: 'blur(4px)',
            zIndex: 1000,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            padding: '24px',
          }}
          onClick={() => {
            setShowCreateModal(false);
            setEditingInvoiceId(null);
          }}
        >
          <div
            onClick={(e) => e.stopPropagation()}
            style={{
              background: '#F8FAFC',
              borderRadius: '16px',
              width: '100%',
              maxWidth: '1240px',
              maxHeight: '92vh',
              overflowY: 'auto',
              boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
              border: '1px solid #E2E8F0',
              padding: '24px',
              display: 'flex',
              flexDirection: 'column',
              boxSizing: 'border-box',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', borderBottom: '1px solid #E2E8F0', paddingBottom: '12px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <i className="ti ti-file-plus" style={{ fontSize: '20px', color: '#0D9488' }} />
                <h2 style={{ margin: 0, fontSize: '18px', fontWeight: 800, color: '#0F172A' }}>
                  {editingInvoiceId ? 'Edit Invoice' : 'Create New Invoice'}
                </h2>
              </div>
              <button
                type="button"
                onClick={() => {
                  setShowCreateModal(false);
                  setEditingInvoiceId(null);
                  refetchInvoices();
                }}
                style={{
                  background: '#F1F5F9',
                  border: 'none',
                  borderRadius: '50%',
                  width: '32px',
                  height: '32px',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  cursor: 'pointer',
                  color: '#64748B',
                }}
              >
                <i className="ti ti-x" style={{ fontSize: '16px' }} />
              </button>
            </div>

            <InvoiceCreation
              isModal={true}
              editId={editingInvoiceId || undefined}
              onClose={() => {
                setShowCreateModal(false);
                setEditingInvoiceId(null);
                refetchInvoices();
              }}
              onSuccess={() => {
                setShowCreateModal(false);
                setEditingInvoiceId(null);
                refetchInvoices();
              }}
            />
          </div>
        </div>
      )}

      {/* Modal on top of list without route change */}
      <InvoiceModal
        isOpen={!!modalInvoiceId}
        invoiceId={modalInvoiceId}
        initialInvoice={modalInvoice}
        autoPrint={isAutoPrint}
        onSendToStars={handleSendToStars}
        onClose={() => {
          setModalInvoiceId(null);
          setModalInvoice(null);
          setIsAutoPrint(false);
        }}
      />
    </div>
  );
};

export default InvoicingDesk;
