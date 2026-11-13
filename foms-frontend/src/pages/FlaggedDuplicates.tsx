import React, { useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { useFlaggedDuplicates } from '../hooks/useDuplicates';
import { useQuery } from '../hooks/useQuery';
import { duplicatesService, type DuplicateFlagItem } from '../services/duplicates.service';
import { DataTable, type Column } from '../components/shared/DataTable';
import FilterBar from '../components/shared/FilterBar';
import StatusBadge from '../components/shared/StatusBadge';
import Button from '../components/Buttons';
import ConfirmModal from '../components/shared/ConfirmModal';
import { useToast } from '../components/ToastContext';
import {
  canDecideDuplicate,
  canReviewDuplicate,
  canApproveOwnCheck,
  DUPLICATE_STATUS_LABELS,
} from '../utils/duplicateWorkflow';
import { DuplicateReviewPanel } from '../components/ai/DuplicateReviewPanel';
import { ROLES } from '../types/auth';
import './FlaggedDuplicates.css';

type ActionModalType = 'mark_duplicate' | 'mark_not_duplicate' | 'approve' | 'return' | null;

export const FlaggedDuplicates: React.FC = () => {
  const { user } = useAuth();
  const { toast } = useToast();

  const isHeadAccountant = user?.role === ROLES.HEAD_ACCOUNTANT;
  // User instruction: "wag mo lagay sa pending, dapat sa all sya" -> default to '' (All)
  const initialStatus = '';

  const {
    items,
    total,
    isLoading,
    error,
    refetch,
    page,
    setPage,
    pageSize,
    search,
    setSearch,
    statusFilter,
    setStatusFilter,
    dateFrom,
    setDateFrom,
    applyFilters,
    resetFilters,
    scope,
  } = useFlaggedDuplicates({ initialStatus });

  // Query stats summary for KPI cards and quick-status tabs
  const summaryQuery = useQuery(
    `duplicates:summary-stats:${scope}`,
    () => duplicatesService.flagged({ scope, pageSize: 100 }),
    { enabled: !!user }
  );

  const statItems = summaryQuery.data?.items || items;
  const totalCount = summaryQuery.data?.total || statItems.length;
  const pendingReviewCount = statItems.filter((i) => i.status === 'PENDING_REVIEW').length;
  const flaggedCount = statItems.filter((i) => i.status === 'FLAGGED').length;
  const confirmedCount = statItems.filter(
    (i) => i.status === 'APPROVED_DUPLICATE' || i.decision === 'DUPLICATE'
  ).length;
  const clearedCount = statItems.filter(
    (i) => i.status === 'CLEARED_NOT_DUPLICATE' || i.decision === 'NOT_DUPLICATE'
  ).length;

  const [activeItem, setActiveItem] = useState<DuplicateFlagItem | null>(null);
  const [modalAction, setModalAction] = useState<ActionModalType>(null);
  const [remarks, setRemarks] = useState('');
  const [validationError, setValidationError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [inspectDoc, setInspectDoc] = useState<DuplicateFlagItem | null>(null);
  const [reviewPanelDoc, setReviewPanelDoc] = useState<DuplicateFlagItem | null>(null);

  const currentUserId = user?.id || user?.employeeId;

  const handleOpenAction = (item: DuplicateFlagItem, action: ActionModalType) => {
    setActiveItem(item);
    setModalAction(action);
    setRemarks('');
    setValidationError('');
  };

  const handleCloseModal = () => {
    setActiveItem(null);
    setModalAction(null);
    setRemarks('');
    setValidationError('');
  };

  const handleActionSubmit = async () => {
    if (!activeItem || !modalAction) return;

    // Both Level 1 decisions and Level 2 Return require remarks
    if ((modalAction === 'mark_duplicate' || modalAction === 'mark_not_duplicate' || modalAction === 'return') && !remarks.trim()) {
      setValidationError('Remarks and justification are required for this action.');
      return;
    }

    // Separation of Duties check for Level 2 Approve
    if (modalAction === 'approve') {
      const sodCheck = canApproveOwnCheck(currentUserId, activeItem.decidedBy);
      if (!sodCheck.allowed) {
        setValidationError(sodCheck.reason || 'You cannot approve your own decision.');
        return;
      }
    }

    setIsSubmitting(true);
    setValidationError('');
    try {
      const reviewerName = user?.fullName || user?.employeeId || 'Reviewer';
      if (modalAction === 'mark_duplicate') {
        await duplicatesService.decideDuplicate(activeItem.id, {
          decision: 'DUPLICATE',
          remarks: remarks.trim(),
          reviewer: reviewerName,
        });
        toast.success(`Marked flag ${activeItem.flagId} as Duplicate. Forwarded to Head Accountant for Review.`);
      } else if (modalAction === 'mark_not_duplicate') {
        await duplicatesService.decideDuplicate(activeItem.id, {
          decision: 'NOT_DUPLICATE',
          remarks: remarks.trim(),
          reviewer: reviewerName,
        });
        toast.info(`Marked flag ${activeItem.flagId} as Not Duplicate. Forwarded to Head Accountant for Review.`);
      } else if (modalAction === 'approve') {
        await duplicatesService.reviewDecision(activeItem.id, {
          action: 'APPROVE',
          remarks: remarks.trim() || undefined,
          reviewer: reviewerName,
        });
        toast.success(`Approved decision for ${activeItem.flagId}. Official status updated.`);
      } else if (modalAction === 'return') {
        await duplicatesService.reviewDecision(activeItem.id, {
          action: 'RETURN',
          remarks: remarks.trim(),
          reviewer: reviewerName,
        });
        toast.warning(`Returned flag ${activeItem.flagId} to Accountant with remarks.`);
      }
      handleCloseModal();
      refetch();
      summaryQuery.refetch();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Action failed to execute.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const columns: Column<DuplicateFlagItem>[] = [
    {
      key: 'flagId',
      label: 'Flag ID',
      render: (row) => (
        <span style={{ fontWeight: 700, color: '#0F172A', fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
          {row.flagId}
        </span>
      ),
    },
    {
      key: 'type',
      label: 'MATCH TYPE',
      render: (row) => {
        const sim = row.similarity ?? 0;
        const isExact = sim >= 95;
        return (
          <span
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '4px',
              padding: '3px 9px',
              borderRadius: '6px',
              fontSize: '11px',
              fontWeight: 700,
              background: isExact ? '#FEF2F2' : '#FFFBEB',
              color: isExact ? '#EF4444' : '#D97706',
              border: `1px solid ${isExact ? '#FEE2E2' : '#FEF3C7'}`,
              letterSpacing: '0.02em',
            }}
          >
            <i className={isExact ? 'ti ti-alert-triangle' : 'ti ti-info-circle'} style={{ fontSize: '12px' }} />
            {row.matchType || (isExact ? 'EXACT MATCH' : 'PARTIAL MATCH')}
          </span>
        );
      },
    },
    {
      key: 'uploadedNumber',
      label: isHeadAccountant ? 'Submitted Number' : 'Uploaded Number',
      render: (row) => (
        <span style={{ fontWeight: 600, color: '#1E293B', fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
          {row.uploadedNumber ?? 'N/A'}
        </span>
      ),
    },
    {
      key: 'existingRecord',
      label: 'Existing Record',
      render: (row) => (
        <span style={{ color: '#4338CA', fontWeight: 600, fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
          {row.existingRecord ?? 'N/A'}
        </span>
      ),
    },
    {
      key: 'clientName',
      label: 'CLIENT NAME',
      render: (row) => <span style={{ fontWeight: 500, color: '#334155' }}>{row.clientName ?? 'N/A'}</span>,
    },
    {
      key: 'similarity',
      label: 'SIMILARITY',
      render: (row) => {
        const sim = row.similarity ?? 0;
        const isHigh = sim >= 95;
        return (
          <span
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '4px',
              fontWeight: 700,
              fontSize: '12px',
              color: isHigh ? '#EF4444' : '#D97706',
              background: isHigh ? '#FEF2F2' : '#FFFBEB',
              padding: '2px 8px',
              borderRadius: '6px',
            }}
          >
            {row.similarity !== undefined ? `${row.similarity}%` : 'N/A'}
          </span>
        );
      },
    },
    {
      key: 'status',
      label: 'STATUS',
      render: (row) => (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <StatusBadge status={row.status} />
          {row.decidedRemarks && (
            <span
              style={{
                fontSize: '11px',
                color: '#64748B',
                fontStyle: 'italic',
                maxWidth: 180,
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
              }}
              title={row.decidedRemarks}
            >
              "{row.decidedRemarks}"
            </span>
          )}
        </div>
      ),
    },
  ];

  const filterFields = [
    {
      key: 'status',
      label: 'STATUS',
      type: 'select' as const,
      options: [
        { label: 'All Statuses', value: '' },
        { label: 'Flagged (Unverified)', value: 'FLAGGED' },
        { label: 'Pending Review (Level 2)', value: 'PENDING_REVIEW' },
        { label: 'Returned for Re-decision', value: 'RETURNED' },
        { label: 'Approved Duplicate', value: 'APPROVED_DUPLICATE' },
        { label: 'Cleared (Not Duplicate)', value: 'CLEARED_NOT_DUPLICATE' },
      ],
    },
    {
      key: 'dateFrom',
      label: 'FLAGGED DATE',
      type: 'date' as const,
    },
  ];

  const filterValues = {
    status: statusFilter,
    dateFrom,
  };

  const handleFieldChange = (key: string, value: string) => {
    if (key === 'status') setStatusFilter(value);
    if (key === 'dateFrom') setDateFrom(value);
  };

  const handleExport = () => {
    const csvContent =
      'data:text/csv;charset=utf-8,' +
      ['Flag ID,Uploaded Number,Existing Record,Client,Amount,Similarity,Status'].join(',') +
      '\n' +
      items
        .map(
          (e) =>
            `"${e.flagId}","${e.uploadedNumber || ''}","${e.existingRecord || ''}","${e.clientName || ''}",${
              e.amount || 0
            },${e.similarity || 0},"${e.status || ''}"`
        )
        .join('\n');
    const encodedUri = encodeURI(csvContent);
    const link = document.createElement('a');
    link.setAttribute('href', encodedUri);
    link.setAttribute('download', `flagged_duplicates_${Date.now()}.csv`);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  const handleSelectQuickStatus = (statusValue: string) => {
    setStatusFilter(statusValue);
    setTimeout(() => {
      applyFilters();
    }, 0);
  };

  return (
    <div className="fd-container">
      {/* Speedex OneUI Header */}
      <div className="fd-header">
        <div>
          <div className="fd-header-title-row">
            <h2 className="fd-header-title">Flagged Duplicate Documents</h2>
            <span className="fd-badge">
              <i className="ti ti-sparkles" style={{ fontSize: '11px' }} />
              {isHeadAccountant ? 'AI AUDIT ENGINE • LEVEL 2 REVIEW' : 'AI AUDIT ENGINE • DUPLICATE WORKFLOW'}
            </span>
          </div>
          <p className="fd-header-desc">
            Review and resolve documents flagged by the AI duplicate detection system.
          </p>
        </div>

        <div className="fd-header-actions">
          <Button
            title="Refresh"
            variant="secondary"
            size="sm"
            onClick={() => {
              refetch();
              summaryQuery.refetch();
            }}
          />
          <Button
            title="Export"
            variant="secondary"
            size="sm"
            onClick={handleExport}
          />
        </div>
      </div>

      {/* Speedex OneUI Metric Summary Bar */}
      <div className="fd-stat-grid">
        <div
          className={`fd-stat-card ${!statusFilter ? 'active' : ''}`}
          onClick={() => handleSelectQuickStatus('')}
          title="Show all flagged documents"
        >
          <div className="fd-stat-icon" style={{ background: '#F1F5F9', color: '#475569' }}>
            <i className="ti ti-files" />
          </div>
          <div className="fd-stat-info">
            <span className="fd-stat-label">Total in Queue</span>
            <span className="fd-stat-value">{totalCount}</span>
          </div>
        </div>

        <div
          className={`fd-stat-card ${statusFilter === 'PENDING_REVIEW' ? 'active' : ''}`}
          onClick={() => handleSelectQuickStatus('PENDING_REVIEW')}
          title="Filter to documents awaiting Level 2 Review"
        >
          <div className="fd-stat-icon" style={{ background: '#FFF7ED', color: '#EA580C' }}>
            <i className="ti ti-clock-hour-4" />
          </div>
          <div className="fd-stat-info">
            <span className="fd-stat-label">Pending Level 2</span>
            <span className="fd-stat-value" style={{ color: '#EA580C' }}>
              {pendingReviewCount}
            </span>
          </div>
        </div>

        <div
          className={`fd-stat-card ${statusFilter === 'FLAGGED' ? 'active' : ''}`}
          onClick={() => handleSelectQuickStatus('FLAGGED')}
          title="Filter to unverified flags requiring decision"
        >
          <div className="fd-stat-icon" style={{ background: '#FEF2F2', color: '#DC2626' }}>
            <i className="ti ti-alert-triangle" />
          </div>
          <div className="fd-stat-info">
            <span className="fd-stat-label">Unverified Flags</span>
            <span className="fd-stat-value" style={{ color: '#DC2626' }}>
              {flaggedCount}
            </span>
          </div>
        </div>

        <div
          className={`fd-stat-card ${statusFilter === 'APPROVED_DUPLICATE' ? 'active' : ''}`}
          onClick={() => handleSelectQuickStatus('APPROVED_DUPLICATE')}
          title="Filter to confirmed duplicates"
        >
          <div className="fd-stat-icon" style={{ background: '#EDE9FE', color: '#7C3AED' }}>
            <i className="ti ti-copy" />
          </div>
          <div className="fd-stat-info">
            <span className="fd-stat-label">Confirmed Duplicates</span>
            <span className="fd-stat-value" style={{ color: '#7C3AED' }}>
              {confirmedCount}
            </span>
          </div>
        </div>

        <div
          className={`fd-stat-card ${statusFilter === 'CLEARED_NOT_DUPLICATE' ? 'active' : ''}`}
          onClick={() => handleSelectQuickStatus('CLEARED_NOT_DUPLICATE')}
          title="Filter to cleared false positives"
        >
          <div className="fd-stat-icon" style={{ background: '#F0FDF4', color: '#16A34A' }}>
            <i className="ti ti-shield-check" />
          </div>
          <div className="fd-stat-info">
            <span className="fd-stat-label">Cleared / Unique</span>
            <span className="fd-stat-value" style={{ color: '#16A34A' }}>
              {clearedCount}
            </span>
          </div>
        </div>
      </div>

      {/* Speedex OneUI Filter Bar */}
      <FilterBar
        searchValue={search}
        onSearchChange={setSearch}
        searchPlaceholder="Search flags, doc number, client…"
        fields={filterFields}
        values={filterValues}
        onFieldChange={handleFieldChange}
        onApply={applyFilters}
        onReset={resetFilters}
      />

      {/* Speedex OneUI Quick Status Filter Bar */}
      <div className="fd-quick-status-row">
        <span className="fd-quick-status-label">Quick Status:</span>
        <button
          type="button"
          className={`fd-pill ${!statusFilter ? 'active' : ''}`}
          onClick={() => handleSelectQuickStatus('')}
        >
          All <span className="fd-pill-badge">{totalCount}</span>
        </button>
        <button
          type="button"
          className={`fd-pill ${statusFilter === 'PENDING_REVIEW' ? 'active' : ''}`}
          onClick={() => handleSelectQuickStatus('PENDING_REVIEW')}
        >
          Pending Review (Level 2) <span className="fd-pill-badge">{pendingReviewCount}</span>
        </button>
        <button
          type="button"
          className={`fd-pill ${statusFilter === 'FLAGGED' ? 'active' : ''}`}
          onClick={() => handleSelectQuickStatus('FLAGGED')}
        >
          Unverified Flags <span className="fd-pill-badge">{flaggedCount}</span>
        </button>
        <button
          type="button"
          className={`fd-pill ${statusFilter === 'APPROVED_DUPLICATE' ? 'active' : ''}`}
          onClick={() => handleSelectQuickStatus('APPROVED_DUPLICATE')}
        >
          Confirmed Duplicates <span className="fd-pill-badge">{confirmedCount}</span>
        </button>
        <button
          type="button"
          className={`fd-pill ${statusFilter === 'CLEARED_NOT_DUPLICATE' ? 'active' : ''}`}
          onClick={() => handleSelectQuickStatus('CLEARED_NOT_DUPLICATE')}
        >
          Cleared / Unique <span className="fd-pill-badge">{clearedCount}</span>
        </button>
        {(statusFilter || search || dateFrom) && (
          <button
            type="button"
            onClick={resetFilters}
            style={{
              marginLeft: 'auto',
              background: 'none',
              border: 'none',
              color: '#64748B',
              fontSize: '12px',
              fontWeight: 600,
              cursor: 'pointer',
              display: 'flex',
              alignItems: 'center',
              gap: '4px',
            }}
          >
            <i className="ti ti-x" /> Clear filters
          </button>
        )}
      </div>

      {/* Main Data Table */}
      <DataTable
        rows={items}
        columns={columns}
        rowKey={(r) => r.id}
        total={total}
        page={page}
        pageSize={pageSize}
        onPageChange={setPage}
        isLoading={isLoading}
        error={error}
        onRetry={refetch}
        emptyMessage="No flagged duplicate documents found."
        renderActions={(row) => {
          const canDecide = canDecideDuplicate(user?.role, row.status, row.documentType);
          const canReview = canReviewDuplicate(user?.role, row.status, row.documentType);
          const sodCheck = canApproveOwnCheck(currentUserId, row.decidedBy);

          return (
            <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', alignItems: 'center' }}>
              <Button
                title="Inspect"
                variant="secondary"
                size="sm"
                onClick={() => setInspectDoc(row)}
              />

              {/* Level 1: Accountant / Asst FM Decision Actions */}
              {canDecide && (
                <>
                  <Button
                    title="Mark Real Duplicate"
                    variant="danger"
                    size="sm"
                    onClick={() => handleOpenAction(row, 'mark_duplicate')}
                  />
                  <Button
                    title="Mark False Positive"
                    variant="secondary"
                    size="sm"
                    onClick={() => handleOpenAction(row, 'mark_not_duplicate')}
                  />
                </>
              )}

              {/* Level 2: Head Accountant / FM Review Actions */}
              {canReview && (
                <>
                  <Button
                    title="Review"
                    variant="primary"
                    size="sm"
                    onClick={() => setReviewPanelDoc(row)}
                  />
                  {sodCheck.allowed ? (
                    <Button
                      title="Approve"
                      variant="success"
                      size="sm"
                      onClick={() => handleOpenAction(row, 'approve')}
                    />
                  ) : (
                    <span
                      style={{
                        fontSize: '0.75rem',
                        color: '#EF4444',
                        fontWeight: 600,
                        padding: '4px 8px',
                        background: '#FEF2F2',
                        borderRadius: 4,
                        border: '1px solid #FECACA',
                      }}
                      title="You cannot approve your own decision"
                    >
                      Cannot Self-Approve
                    </span>
                  )}
                  <Button
                    title="Return"
                    variant="secondary"
                    size="sm"
                    onClick={() => handleOpenAction(row, 'return')}
                  />
                </>
              )}
            </div>
          );
        }}
      />

      {/* Level 1 & Level 2 Action Modal */}
      {modalAction && activeItem && (
        <ConfirmModal
          isOpen={true}
          title={
            modalAction === 'mark_duplicate'
              ? `Mark Real Duplicate — ${activeItem.flagId}`
              : modalAction === 'mark_not_duplicate'
              ? `Mark False Positive — ${activeItem.flagId}`
              : modalAction === 'approve'
              ? `Approve Decision — ${activeItem.flagId}`
              : `Return to Accountant — ${activeItem.flagId}`
          }
          message={
            modalAction === 'mark_duplicate'
              ? `Marking ${activeItem.uploadedNumber} as a duplicate of ${activeItem.existingRecord}. This moves the record to PENDING_REVIEW for Head Accountant supervisor approval.`
              : modalAction === 'mark_not_duplicate'
              ? `Marking ${activeItem.uploadedNumber} as legitimate and NOT a duplicate. This moves the record to PENDING_REVIEW for Head Accountant supervisor approval.`
              : modalAction === 'approve'
              ? `Approve the Accountant's decision on ${activeItem.flagId}? Official system records will be updated upon your approval.`
              : `Returning flag ${activeItem.flagId} back to the Accountant for re-examination. Please provide required remarks explaining why.`
          }
          variant={modalAction === 'mark_duplicate' ? 'danger' : 'primary'}
          confirmLabel={
            isSubmitting
              ? 'Processing…'
              : modalAction === 'mark_duplicate'
              ? 'Mark Real Duplicate'
              : modalAction === 'mark_not_duplicate'
              ? 'Mark False Positive'
              : modalAction === 'approve'
              ? 'Approve'
              : 'Return'
          }
          onCancel={handleCloseModal}
          onConfirm={handleActionSubmit}
        >
          <div style={{ marginTop: 12 }}>
            <label style={{ display: 'block', fontSize: '0.8rem', fontWeight: 600, color: '#475569', marginBottom: 4 }}>
              {modalAction === 'approve'
                ? 'Approval Notes (Optional)'
                : 'Justification Remarks (Required) *'}
            </label>
            <textarea
              value={remarks}
              onChange={(e) => {
                setRemarks(e.target.value);
                if (validationError) setValidationError('');
              }}
              placeholder={
                modalAction === 'mark_duplicate'
                  ? 'Required: Explain why this record is confirmed as a duplicate...'
                  : modalAction === 'mark_not_duplicate'
                  ? 'Required: Explain why this document is unique and not a duplicate...'
                  : modalAction === 'return'
                  ? 'Required: Explain why the decision is returned to the Accountant...'
                  : 'Optional approval notes...'
              }
              rows={3}
              style={{
                width: '100%',
                padding: '8px 10px',
                borderRadius: '6px',
                border: `1px solid ${validationError ? '#EF4444' : '#CBD5E1'}`,
                fontSize: '0.85rem',
                fontFamily: 'inherit',
                boxSizing: 'border-box',
              }}
            />
            {validationError && (
              <span style={{ color: '#DC2626', fontSize: '0.78rem', fontWeight: 600, marginTop: 4, display: 'block' }}>
                {validationError}
              </span>
            )}
          </div>
        </ConfirmModal>
      )}

      {/* Level 2 Side-by-Side Review Panel */}
      {reviewPanelDoc && (
        <DuplicateReviewPanel
          isOpen={true}
          item={reviewPanelDoc}
          onClose={() => setReviewPanelDoc(null)}
          onApprove={async (apprRemarks) => {
            try {
              await duplicatesService.reviewDecision(reviewPanelDoc.id, {
                action: 'APPROVE',
                remarks: apprRemarks,
                reviewer: user?.fullName || user?.employeeId || 'Reviewer',
              });
              toast.success(`Approved duplicate decision for ${reviewPanelDoc.flagId}.`);
              setReviewPanelDoc(null);
              refetch();
              summaryQuery.refetch();
            } catch (err) {
              toast.error(err instanceof Error ? err.message : 'Approval failed.');
            }
          }}
          onReturn={async (retRemarks) => {
            try {
              await duplicatesService.reviewDecision(reviewPanelDoc.id, {
                action: 'RETURN',
                remarks: retRemarks,
                reviewer: user?.fullName || user?.employeeId || 'Reviewer',
              });
              toast.warning(`Returned flag ${reviewPanelDoc.flagId} to Accountant.`);
              setReviewPanelDoc(null);
              refetch();
              summaryQuery.refetch();
            } catch (err) {
              toast.error(err instanceof Error ? err.message : 'Return failed.');
            }
          }}
        />
      )}

      {/* Speedex OneUI Inspect Modal */}
      {inspectDoc && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(15, 23, 42, 0.6)',
            backdropFilter: 'blur(3px)',
            zIndex: 9999,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            padding: 20,
          }}
          onClick={() => setInspectDoc(null)}
        >
          <div
            style={{
              background: '#FFFFFF',
              borderRadius: 16,
              maxWidth: 540,
              width: '100%',
              boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
              border: '1px solid #E2E8F0',
              overflow: 'hidden',
            }}
            onClick={(e) => e.stopPropagation()}
          >
            {/* Modal Header */}
            <div
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                padding: '18px 24px',
                borderBottom: '1px solid #F1F5F9',
                background: '#FAFAFA',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <div
                  style={{
                    width: 34,
                    height: 34,
                    borderRadius: 8,
                    background: '#F0FDFA',
                    color: '#00A99D',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                  }}
                >
                  <i className="ti ti-file-search" style={{ fontSize: '18px' }} />
                </div>
                <div>
                  <h3 style={{ margin: 0, fontSize: '1.05rem', fontWeight: 800, color: '#0F172A', fontFamily: 'Montserrat, sans-serif' }}>
                    Document Inspection
                  </h3>
                  <span style={{ fontSize: '11px', color: '#64748B', fontFamily: 'JetBrains Mono, monospace' }}>
                    {inspectDoc.flagId}
                  </span>
                </div>
              </div>
              <button
                type="button"
                onClick={() => setInspectDoc(null)}
                style={{
                  background: 'none',
                  border: 'none',
                  fontSize: '20px',
                  cursor: 'pointer',
                  color: '#94A3B8',
                  padding: 4,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                &times;
              </button>
            </div>

            {/* Modal Body */}
            <div style={{ padding: '20px 24px', display: 'flex', flexDirection: 'column', gap: 12, fontSize: '0.85rem' }}>
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  padding: '8px 12px',
                  background: '#F8FAFC',
                  borderRadius: 8,
                }}
              >
                <span style={{ color: '#64748B', fontWeight: 600 }}>Source Document:</span>
                <strong style={{ fontFamily: 'JetBrains Mono, monospace', color: '#0F172A' }}>
                  {inspectDoc.uploadedNumber}
                </strong>
              </div>

              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  padding: '8px 12px',
                  background: '#F8FAFC',
                  borderRadius: 8,
                }}
              >
                <span style={{ color: '#64748B', fontWeight: 600 }}>Matched Existing Record:</span>
                <strong style={{ fontFamily: 'JetBrains Mono, monospace', color: '#4338CA' }}>
                  {inspectDoc.existingRecord}
                </strong>
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', padding: '0 4px' }}>
                <span style={{ color: '#64748B' }}>Client Name:</span>
                <span style={{ fontWeight: 600, color: '#334155' }}>{inspectDoc.clientName}</span>
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', padding: '0 4px' }}>
                <span style={{ color: '#64748B' }}>AI Similarity:</span>
                <strong
                  style={{
                    color: (inspectDoc.similarity ?? 0) >= 95 ? '#EF4444' : '#D97706',
                    background: (inspectDoc.similarity ?? 0) >= 95 ? '#FEF2F2' : '#FFFBEB',
                    padding: '2px 8px',
                    borderRadius: 6,
                  }}
                >
                  {inspectDoc.similarity !== undefined ? `${inspectDoc.similarity}%` : 'N/A'}
                </strong>
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', padding: '0 4px' }}>
                <span style={{ color: '#64748B' }}>Match Reason:</span>
                <span style={{ color: '#334155', maxWidth: 300, textAlign: 'right' }}>{inspectDoc.duplicateReason}</span>
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', padding: '0 4px', alignItems: 'center' }}>
                <span style={{ color: '#64748B' }}>Workflow Status:</span>
                <StatusBadge status={inspectDoc.status} />
              </div>

              {inspectDoc.decision && (
                <div style={{ display: 'flex', justifyContent: 'space-between', padding: '0 4px' }}>
                  <span style={{ color: '#64748B' }}>Level 1 Decision:</span>
                  <strong>{inspectDoc.decision}</strong>
                </div>
              )}

              {inspectDoc.decidedRemarks && (
                <div style={{ background: '#FFFBEB', border: '1px solid #FEF3C7', padding: '10px 14px', borderRadius: 8, marginTop: 4 }}>
                  <span style={{ color: '#B45309', display: 'block', fontSize: '0.75rem', fontWeight: 700, textTransform: 'uppercase', marginBottom: 2 }}>
                    Accountant Decision Remarks:
                  </span>
                  <span style={{ color: '#78350F', fontSize: '0.82rem' }}>{inspectDoc.decidedRemarks}</span>
                </div>
              )}
            </div>

            {/* Modal Footer */}
            <div
              style={{
                padding: '14px 24px',
                background: '#FAFAFA',
                borderTop: '1px solid #F1F5F9',
                display: 'flex',
                justifyContent: 'flex-end',
              }}
            >
              <Button title="Close" variant="secondary" size="sm" onClick={() => setInspectDoc(null)} />
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default FlaggedDuplicates;
