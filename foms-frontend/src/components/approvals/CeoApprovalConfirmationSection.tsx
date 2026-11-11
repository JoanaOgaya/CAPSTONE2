import React, { useState, useEffect } from 'react';
import { useAuth } from '../../context/AuthContext';
import { approvalsService, formatApprovalErrorMessage } from '../../services/approvalsService';
import { canConfirmCeoApproval } from '../../utils/roleAccess';
import { formatDateTime } from '../../utils/format';

export interface CeoApprovalItem {
  recordId: number;
  itemType: string;
  itemId: string;
  status: string;
  approverLabel: string;
  enteredBy: string;
  enteredAt: string;
  approvalDate: string;
  notes?: string;
  hasAttachment?: boolean;
  attachmentFileName?: string;
  confirmations?: Array<{
    confirmationId: number;
    recordId: number;
    kind: string;
    confirmedBy: string;
    confirmedAt: string;
  }>;
}

export interface CeoApprovalConfirmationSectionProps {
  initialItems?: CeoApprovalItem[];
  onConfirmed?: (recordId: number) => void;
}

export const CeoApprovalConfirmationSection: React.FC<CeoApprovalConfirmationSectionProps> = ({
  initialItems,
  onConfirmed,
}) => {
  const { user } = useAuth();
  const [items, setItems] = useState<CeoApprovalItem[]>(initialItems || []);
  const [isLoading, setIsLoading] = useState(!initialItems);
  const [error, setError] = useState<string | null>(null);

  // Confirmation modal/action state
  const [selectedRecord, setSelectedRecord] = useState<CeoApprovalItem | null>(null);
  const [note, setNote] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);

  const canConfirm = canConfirmCeoApproval(user?.role);

  const fetchItems = async () => {
    try {
      setIsLoading(true);
      setError(null);
      const data = await approvalsService.getUnconfirmedCeoApprovals();
      setItems(data);
    } catch (err) {
      setError(formatApprovalErrorMessage(err));
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    if (!initialItems) {
      fetchItems();
    }
  }, [initialItems]);

  const handleOpenConfirm = (record: CeoApprovalItem) => {
    setSelectedRecord(record);
    setNote('');
    setActionError(null);
    setActionSuccess(null);
  };

  const handleCloseConfirm = () => {
    setSelectedRecord(null);
    setNote('');
    setActionError(null);
  };

  const handleSubmitConfirm = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedRecord) return;

    try {
      setIsSubmitting(true);
      setActionError(null);
      await approvalsService.confirmCeoApproval(selectedRecord.recordId, note);
      setActionSuccess(`CEO approval record #${selectedRecord.recordId} confirmed successfully.`);
      setItems(prev => prev.filter(i => i.recordId !== selectedRecord.recordId));
      if (onConfirmed) {
        onConfirmed(selectedRecord.recordId);
      }
      setTimeout(() => {
        handleCloseConfirm();
      }, 800);
    } catch (err) {
      setActionError(formatApprovalErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="ceo-approval-confirmation-section" style={{ background: '#fff', borderRadius: 12, padding: 20, boxShadow: '0 1px 3px rgba(0,0,0,0.05)', border: '1px solid #E2E8F0' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <div>
          <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: '#0F172A' }}>
            CEO Approval Records Pending Confirmation
          </h3>
          <p style={{ margin: '4px 0 0', fontSize: '0.8rem', color: '#64748B' }}>
            Maker-checker recording confirmation for budget transfers and carry-overs (R5.11 item 1).
          </p>
        </div>
        {!initialItems && (
          <button
            type="button"
            onClick={fetchItems}
            disabled={isLoading}
            style={{ padding: '6px 12px', fontSize: '0.8rem', background: '#F1F5F9', border: '1px solid #CBD5E1', borderRadius: 6, cursor: 'pointer' }}
          >
            {isLoading ? 'Refreshing...' : 'Refresh'}
          </button>
        )}
      </div>

      {error && (
        <div style={{ padding: 12, background: '#FEF2F2', border: '1px solid #F87171', borderRadius: 8, color: '#991B1B', fontSize: '0.85rem', marginBottom: 16 }}>
          {error}
        </div>
      )}

      {isLoading ? (
        <p style={{ color: '#64748B', fontSize: '0.85rem' }}>Loading CEO approval records...</p>
      ) : items.length === 0 ? (
        <p style={{ color: '#64748B', fontSize: '0.85rem', margin: '8px 0' }}>
          No budget transfers or carry-overs awaiting Head Accountant confirmation.
        </p>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {items.map(item => (
            <div
              key={item.recordId}
              data-testid={`ceo-approval-item-${item.recordId}`}
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                padding: 14,
                borderRadius: 8,
                background: '#F8FAFC',
                border: '1px solid #E2E8F0',
              }}
            >
              <div>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 4 }}>
                  <span style={{ fontSize: '0.75rem', fontWeight: 700, padding: '2px 8px', borderRadius: 4, background: '#DBEAFE', color: '#1E40AF' }}>
                    {item.itemType}
                  </span>
                  <span style={{ fontWeight: 600, fontSize: '0.9rem', color: '#0F172A' }}>
                    ID: {item.itemId}
                  </span>
                  <span style={{ fontSize: '0.75rem', color: '#64748B' }}>
                    Record #{item.recordId}
                  </span>
                </div>
                <div style={{ fontSize: '0.8rem', color: '#475569' }}>
                  Entered by: <strong>{item.enteredBy}</strong> | Approval Date: {formatDateTime(item.approvalDate)}
                </div>
                {item.notes && (
                  <div style={{ fontSize: '0.75rem', color: '#64748B', marginTop: 2, fontStyle: 'italic' }}>
                    Note: {item.notes}
                  </div>
                )}
              </div>

              {canConfirm && (
                <button
                  type="button"
                  data-testid={`confirm-btn-${item.recordId}`}
                  onClick={() => handleOpenConfirm(item)}
                  style={{
                    padding: '8px 16px',
                    fontSize: '0.82rem',
                    fontWeight: 600,
                    background: '#0D9488',
                    color: '#fff',
                    border: 'none',
                    borderRadius: 6,
                    cursor: 'pointer',
                  }}
                >
                  Confirm Recording
                </button>
              )}
            </div>
          ))}
        </div>
      )}

      {selectedRecord && (
        <div
          data-testid="confirm-modal"
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(0,0,0,0.5)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 1000,
          }}
        >
          <div style={{ background: '#fff', borderRadius: 12, padding: 24, maxWidth: 480, width: '100%', boxShadow: '0 20px 25px -5px rgba(0,0,0,0.1)' }}>
            <h4 style={{ margin: '0 0 8px 0', fontSize: '1.1rem', color: '#0F172A' }}>
              Confirm CEO Approval Recording
            </h4>
            <p style={{ fontSize: '0.85rem', color: '#475569', margin: '0 0 16px 0' }}>
              You are confirming the recording of CEO/President approval for {selectedRecord.itemType} #{selectedRecord.itemId} (Record #{selectedRecord.recordId}).
              This acts as a maker-checker verification of the record and enables the budget transfer to be applied.
            </p>

            {actionError && (
              <div style={{ padding: 10, background: '#FEF2F2', border: '1px solid #F87171', borderRadius: 6, color: '#991B1B', fontSize: '0.82rem', marginBottom: 12 }}>
                {actionError}
              </div>
            )}

            {actionSuccess && (
              <div style={{ padding: 10, background: '#ECFDF5', border: '1px solid #6EE7B7', borderRadius: 6, color: '#065F46', fontSize: '0.82rem', marginBottom: 12 }}>
                {actionSuccess}
              </div>
            )}

            <form onSubmit={handleSubmitConfirm}>
              <div style={{ marginBottom: 16 }}>
                <label htmlFor="confirm-note" style={{ display: 'block', fontSize: '0.82rem', fontWeight: 600, color: '#334155', marginBottom: 6 }}>
                  Optional Confirmation Note
                </label>
                <textarea
                  id="confirm-note"
                  data-testid="confirm-note-input"
                  value={note}
                  onChange={e => setNote(e.target.value)}
                  placeholder="e.g. Verified with signed Board Resolution copy"
                  rows={3}
                  style={{ width: '100%', padding: '8px 10px', borderRadius: 6, border: '1px solid #CBD5E1', fontSize: '0.85rem' }}
                />
              </div>

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10 }}>
                <button
                  type="button"
                  onClick={handleCloseConfirm}
                  disabled={isSubmitting}
                  style={{ padding: '8px 16px', fontSize: '0.85rem', background: '#F1F5F9', border: '1px solid #CBD5E1', borderRadius: 6, cursor: 'pointer' }}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  data-testid="submit-confirm-btn"
                  disabled={isSubmitting}
                  style={{ padding: '8px 16px', fontSize: '0.85rem', fontWeight: 600, background: '#0D9488', color: '#fff', border: 'none', borderRadius: 6, cursor: 'pointer' }}
                >
                  {isSubmitting ? 'Confirming...' : 'Confirm'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
