import { useMemo, useState, type FormEvent } from 'react';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../components/ToastContext';
import { useQuery } from '../hooks/useQuery';
import { paymentsService } from '../services/paymentsService';
import { periodCloseService, type AccountingPeriodClose } from '../services/periodCloseService';
import { Role } from '../types/auth';
import type { Payment } from '../types/entities';
import { Button } from '../components/Buttons';
import StatusBadge from '../components/shared/StatusBadge';
import './PeriodClose.css';

const previousMonth = () => {
  const date = new Date();
  date.setDate(1);
  date.setMonth(date.getMonth() - 1);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
};

const apiErrorMessage = (error: unknown) => {
  if (error instanceof Error) return error.message;
  return 'The accounting operation failed.';
};

const dateForPayment = (payment: Payment) => payment.paymentDate || payment.recordedAt || '';

export default function PeriodClose() {
  const { user } = useAuth();
  const { toast } = useToast();
  const canClose = user?.role === Role.FINANCE_MANAGER;
  const [periodType, setPeriodType] = useState<'Month' | 'Year'>('Month');
  const [periodValue, setPeriodValue] = useState(previousMonth());
  const [bankBookBalance, setBankBookBalance] = useState('');
  const [bankStatementBalance, setBankStatementBalance] = useState('');
  const [bankStatementProofReference, setBankStatementProofReference] = useState('');
  const [cashBookBalance, setCashBookBalance] = useState('');
  const [countedCashBalance, setCountedCashBalance] = useState('');
  const [exceptionReasons, setExceptionReasons] = useState<Record<string, string>>({});
  const [selectedPeriodId, setSelectedPeriodId] = useState('');
  const [correctionDate, setCorrectionDate] = useState(new Date().toISOString().slice(0, 10));
  const [correctionDescription, setCorrectionDescription] = useState('');
  const [debitAccount, setDebitAccount] = useState('');
  const [creditAccount, setCreditAccount] = useState('');
  const [correctionAmount, setCorrectionAmount] = useState('');
  const [saving, setSaving] = useState(false);

  const { data: periods = [], error: periodError, refetch } = useQuery(
    'accounting-period-closes',
    () => periodCloseService.list(),
  );
  const { data: paymentData } = useQuery(
    'period-close-payments',
    () => paymentsService.list({ pageSize: 500 }),
  );
  const payments = paymentData?.items ?? [];

  const periodBounds = useMemo(() => {
    if (periodType === 'Year') {
      const year = Number(periodValue);
      return { start: `${year}-01-01`, end: `${year}-12-31` };
    }

    const [yearText, monthText] = periodValue.split('-');
    const year = Number(yearText);
    const month = Number(monthText);
    const lastDay = new Date(year, month, 0).getDate();
    return {
      start: `${yearText}-${monthText}-01`,
      end: `${yearText}-${monthText}-${String(lastDay).padStart(2, '0')}`,
    };
  }, [periodType, periodValue]);

  const periodExceptions = useMemo(() => payments.filter((payment) => {
    if (payment.status !== 'Exception') return false;
    const date = dateForPayment(payment).slice(0, 10);
    return date >= periodBounds.start && date <= periodBounds.end;
  }), [payments, periodBounds]);

  // Live balance variances calculation
  const bankDiff = useMemo(() => {
    if (!bankBookBalance || !bankStatementBalance) return null;
    return Number(bankStatementBalance) - Number(bankBookBalance);
  }, [bankBookBalance, bankStatementBalance]);

  const cashDiff = useMemo(() => {
    if (!cashBookBalance || !countedCashBalance) return null;
    return Number(countedCashBalance) - Number(cashBookBalance);
  }, [cashBookBalance, countedCashBalance]);

  const submitClose = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canClose || saving) return;

    const amounts = [bankBookBalance, bankStatementBalance, cashBookBalance, countedCashBalance].map(Number);
    if (amounts.some((amount) => !Number.isFinite(amount) || amount < 0)) {
      toast.error('Enter valid non-negative cash and bank balances.');
      return;
    }

    setSaving(true);
    try {
      await periodCloseService.close({
        periodType,
        periodStart: periodBounds.start,
        periodEnd: periodBounds.end,
        bankBookBalance: amounts[0],
        bankStatementBalance: amounts[1],
        bankStatementProofReference: bankStatementProofReference.trim(),
        cashBookBalance: amounts[2],
        countedCashBalance: amounts[3],
        listedExceptions: periodExceptions
          .filter((payment) => exceptionReasons[payment.id]?.trim())
          .map((payment) => ({ paymentId: payment.id, reason: exceptionReasons[payment.id].trim() })),
      });
      toast.success('Accounting period closed and audited.');
      setExceptionReasons({});
      await refetch();
    } catch (error) {
      toast.error(apiErrorMessage(error), 'Period Close Failed');
    } finally {
      setSaving(false);
    }
  };

  const submitCorrection = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canClose || !selectedPeriodId || saving) return;

    setSaving(true);
    try {
      await periodCloseService.addCorrection(selectedPeriodId, {
        entryDate: correctionDate,
        description: correctionDescription.trim(),
        debitAccount: debitAccount.trim(),
        creditAccount: creditAccount.trim(),
        amount: Number(correctionAmount),
      });
      toast.success('Correcting entry recorded.');
      setCorrectionDescription('');
      setDebitAccount('');
      setCreditAccount('');
      setCorrectionAmount('');
      await refetch();
    } catch (error) {
      toast.error(apiErrorMessage(error), 'Correction Failed');
    } finally {
      setSaving(false);
    }
  };

  const selectedPeriod = periods.find((p: AccountingPeriodClose) => p.id === selectedPeriodId);

  return (
    <div className="pc-container">
      {/* ── Top Header Banner ── */}
      <header className="pc-header">
        <div>
          <div className="pc-header-title-row">
            <h1 className="pc-header-title">Period Close</h1>
            <span
              style={{
                background: '#E0F2FE',
                color: '#0369A1',
                padding: '4px 10px',
                borderRadius: '12px',
                fontSize: '11px',
                fontWeight: 700,
                letterSpacing: '0.5px',
                textTransform: 'uppercase',
                display: 'inline-flex',
                alignItems: 'center',
                gap: 5,
              }}
            >
              <i className="ti ti-lock" /> Ledger Lock
            </span>
          </div>
          <p className="pc-header-desc">
            Complete the monthly or yearly reconciliation checklist. Closed periods are locked for compliance;
            subsequent adjustments must be posted as balanced offsetting journal entries.
          </p>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <Button
            title="Refresh"
            variant="secondary"
            size="sm"
            icon="ti ti-refresh"
            onClick={() => refetch()}
          />
        </div>
      </header>

      {/* ── Top Metrics Summary Cards ── */}
      <div className="pc-stat-grid">
        <div className="pc-stat-card">
          <div className="pc-stat-icon" style={{ background: '#F0FDFA', color: '#0D9488' }}>
            <i className="ti ti-calendar-event" />
          </div>
          <div className="pc-stat-info">
            <span className="pc-stat-label">Target Cycle</span>
            <span className="pc-stat-value">{periodType === 'Month' ? periodValue : `FY ${periodValue}`}</span>
            <span className="pc-stat-sub">Range: {periodBounds.start.slice(5)} to {periodBounds.end.slice(5)}</span>
          </div>
        </div>

        <div className="pc-stat-card">
          <div
            className="pc-stat-icon"
            style={{
              background: periodExceptions.length === 0 ? '#F0FDF4' : '#FEF2F2',
              color: periodExceptions.length === 0 ? '#16A34A' : '#DC2626',
            }}
          >
            <i className={periodExceptions.length === 0 ? 'ti ti-shield-check' : 'ti ti-alert-triangle'} />
          </div>
          <div className="pc-stat-info">
            <span className="pc-stat-label">Exceptions Queue</span>
            <span className="pc-stat-value">
              {periodExceptions.length === 0 ? '0 Exceptions' : `${periodExceptions.length} Unresolved`}
            </span>
            <span
              className="pc-stat-sub"
              style={{ color: periodExceptions.length === 0 ? '#16A34A' : '#DC2626' }}
            >
              {periodExceptions.length === 0 ? 'Reconciliation Clear' : 'Requires Reason'}
            </span>
          </div>
        </div>

        <div className="pc-stat-card">
          <div className="pc-stat-icon" style={{ background: '#F8FAFC', color: '#1B254B' }}>
            <i className="ti ti-lock-check" />
          </div>
          <div className="pc-stat-info">
            <span className="pc-stat-label">4-Way Match Policy</span>
            <span className="pc-stat-value">Enforced</span>
            <span className="pc-stat-sub" style={{ color: '#0284C7' }}>Payments · Liq · AR · Bank</span>
          </div>
        </div>

        <div className="pc-stat-card">
          <div className="pc-stat-icon" style={{ background: '#F5F3FF', color: '#7C3AED' }}>
            <i className="ti ti-history" />
          </div>
          <div className="pc-stat-info">
            <span className="pc-stat-label">Archived Periods</span>
            <span className="pc-stat-value">{periods.length} Closed</span>
            <span className="pc-stat-sub" style={{ color: '#6D28D9' }}>Locked Historical Snapshots</span>
          </div>
        </div>
      </div>

      {periodError && (
        <div
          role="alert"
          style={{
            padding: '14px 18px',
            background: '#FEF2F2',
            border: '1px solid #FECACA',
            borderRadius: 12,
            color: '#B91C1C',
            display: 'flex',
            alignItems: 'center',
            gap: 10,
            fontSize: '0.88rem',
            fontWeight: 600,
          }}
        >
          <i className="ti ti-alert-circle" style={{ fontSize: 20 }} />
          <span>{periodError.message}</span>
        </div>
      )}

      {/* ── Form Card: Close a Period ── */}
      {canClose && (
        <form onSubmit={submitClose} className="pc-card">
          <div className="pc-card-header">
            <div className="pc-card-title-group">
              <div className="pc-card-header-icon">
                <i className="ti ti-calendar-check" />
              </div>
              <div>
                <h2 className="pc-card-title">Close a period</h2>
                <p className="pc-card-subtitle">
                  Input verified bank and physical cash balances to reconcile and lock the accounting period.
                </p>
              </div>
            </div>

            <span
              style={{
                background: '#F0FDFA',
                color: '#0F766E',
                border: '1px solid #99F6E4',
                padding: '5px 12px',
                borderRadius: '20px',
                fontSize: '11.5px',
                fontWeight: 700,
                letterSpacing: '0.4px',
                display: 'inline-flex',
                alignItems: 'center',
                gap: 6,
              }}
            >
              <i className="ti ti-shield-check" style={{ fontSize: 15 }} />
              Finance Manager Final Authority
            </span>
          </div>

          {/* Form Fields Grid */}
          <div className="pc-form-grid">
            <div className="pc-field">
              <label className="pc-label" htmlFor="pc-period-type">
                Period type <span className="pc-label-req">*</span>
              </label>
              <div className="pc-input-wrapper">
                <div className="pc-input-prefix">
                  <i className="ti ti-calendar" />
                </div>
                <select
                  id="pc-period-type"
                  value={periodType}
                  onChange={(event) => {
                    const type = event.target.value as 'Month' | 'Year';
                    setPeriodType(type);
                    setPeriodValue((current) => type === 'Year' ? current.slice(0, 4) : `${current.slice(0, 4)}-01`);
                  }}
                  className="pc-select"
                >
                  <option value="Month">Month</option>
                  <option value="Year">Year</option>
                </select>
              </div>
            </div>

            <div className="pc-field">
              <label className="pc-label" htmlFor="pc-period-val">
                {periodType === 'Month' ? 'Month' : 'Year'} <span className="pc-label-req">*</span>
              </label>
              <div className="pc-input-wrapper">
                <div className="pc-input-prefix">
                  <i className="ti ti-clock-hour-4" />
                </div>
                <input
                  id="pc-period-val"
                  type={periodType === 'Month' ? 'month' : 'number'}
                  min={periodType === 'Year' ? 2000 : undefined}
                  max={periodType === 'Year' ? 9999 : undefined}
                  value={periodValue}
                  onChange={(event) => setPeriodValue(event.target.value)}
                  required
                  className="pc-input"
                />
              </div>
            </div>

            <div className="pc-field">
              <label className="pc-label" htmlFor="pc-bank-book">
                Bank book balance <span className="pc-label-req">*</span>
              </label>
              <div className="pc-input-wrapper">
                <div className="pc-input-prefix">₱</div>
                <input
                  id="pc-bank-book"
                  type="number"
                  min="0"
                  step="0.01"
                  value={bankBookBalance}
                  onChange={(event) => setBankBookBalance(event.target.value)}
                  required
                  placeholder="0.00"
                  className="pc-input"
                />
              </div>
            </div>

            <div className="pc-field">
              <label className="pc-label" htmlFor="pc-bank-stmt">
                Bank statement closing balance <span className="pc-label-req">*</span>
              </label>
              <div className="pc-input-wrapper">
                <div className="pc-input-prefix">₱</div>
                <input
                  id="pc-bank-stmt"
                  type="number"
                  min="0"
                  step="0.01"
                  value={bankStatementBalance}
                  onChange={(event) => setBankStatementBalance(event.target.value)}
                  required
                  placeholder="0.00"
                  className="pc-input"
                />
              </div>
            </div>

            <div className="pc-field">
              <label className="pc-label" htmlFor="pc-bank-proof">
                Bank statement proof reference <span className="pc-label-req">*</span>
              </label>
              <div className="pc-input-wrapper">
                <div className="pc-input-prefix">
                  <i className="ti ti-file-certificate" />
                </div>
                <input
                  id="pc-bank-proof"
                  value={bankStatementProofReference}
                  onChange={(event) => setBankStatementProofReference(event.target.value)}
                  required
                  placeholder="Stored document ID or secure path"
                  className="pc-input"
                />
              </div>
            </div>

            <div className="pc-field">
              <label className="pc-label" htmlFor="pc-cash-book">
                Cash book balance <span className="pc-label-req">*</span>
              </label>
              <div className="pc-input-wrapper">
                <div className="pc-input-prefix">₱</div>
                <input
                  id="pc-cash-book"
                  type="number"
                  min="0"
                  step="0.01"
                  value={cashBookBalance}
                  onChange={(event) => setCashBookBalance(event.target.value)}
                  required
                  placeholder="0.00"
                  className="pc-input"
                />
              </div>
            </div>

            <div className="pc-field">
              <label className="pc-label" htmlFor="pc-counted-cash">
                Counted cash balance <span className="pc-label-req">*</span>
              </label>
              <div className="pc-input-wrapper">
                <div className="pc-input-prefix">₱</div>
                <input
                  id="pc-counted-cash"
                  type="number"
                  min="0"
                  step="0.01"
                  value={countedCashBalance}
                  onChange={(event) => setCountedCashBalance(event.target.value)}
                  required
                  placeholder="0.00"
                  className="pc-input"
                />
              </div>
            </div>
          </div>

          {/* Live Variance Bar */}
          {(bankDiff !== null || cashDiff !== null) && (
            <div className="pc-variance-bar">
              <div className="pc-variance-item">
                <span style={{ color: '#64748B' }}>Bank Reconciliation:</span>
                {bankDiff === null ? (
                  <span style={{ color: '#94A3B8' }}>Awaiting values</span>
                ) : bankDiff === 0 ? (
                  <span style={{ color: '#059669', display: 'flex', alignItems: 'center', gap: 4 }}>
                    <i className="ti ti-circle-check-filled" /> Balanced (₱0.00 variance)
                  </span>
                ) : (
                  <span style={{ color: '#D97706', display: 'flex', alignItems: 'center', gap: 4 }}>
                    <i className="ti ti-alert-triangle-filled" /> Variance: ₱{bankDiff.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                  </span>
                )}
              </div>

              <div className="pc-variance-item">
                <span style={{ color: '#64748B' }}>Cash Reconciliation:</span>
                {cashDiff === null ? (
                  <span style={{ color: '#94A3B8' }}>Awaiting values</span>
                ) : cashDiff === 0 ? (
                  <span style={{ color: '#059669', display: 'flex', alignItems: 'center', gap: 4 }}>
                    <i className="ti ti-circle-check-filled" /> Balanced (₱0.00 variance)
                  </span>
                ) : (
                  <span style={{ color: '#D97706', display: 'flex', alignItems: 'center', gap: 4 }}>
                    <i className="ti ti-alert-triangle-filled" /> Variance: ₱{cashDiff.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                  </span>
                )}
              </div>
            </div>
          )}

          {/* Unresolved Payment Exceptions */}
          <div className="pc-section-container">
            <div className="pc-section-header">
              <h3 className="pc-section-title">
                <i className="ti ti-receipt-off" style={{ color: '#D97706' }} />
                Unresolved payment exceptions
              </h3>
              <span
                style={{
                  fontSize: '11px',
                  fontWeight: 700,
                  padding: '3px 9px',
                  borderRadius: 10,
                  background: periodExceptions.length === 0 ? '#DCFCE7' : '#FEF3C7',
                  color: periodExceptions.length === 0 ? '#15803D' : '#B45309',
                }}
              >
                {periodExceptions.length} Listed
              </span>
            </div>

            {periodExceptions.length === 0 ? (
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 12,
                  padding: '14px 18px',
                  background: '#FFFFFF',
                  border: '1px solid #BBF7D0',
                  borderRadius: 10,
                }}
              >
                <i className="ti ti-circle-check-filled" style={{ color: '#16A34A', fontSize: 22 }} />
                <div>
                  <div style={{ fontWeight: 700, color: '#15803D', fontSize: '0.88rem' }}>Reconciliation Clear</div>
                  <div style={{ color: '#166534', fontSize: '0.82rem' }}>
                    No payment exceptions are recorded for this period.
                  </div>
                </div>
              </div>
            ) : (
              <div style={{ display: 'grid', gap: 10 }}>
                {periodExceptions.map((payment) => (
                  <div
                    key={payment.id}
                    style={{
                      display: 'grid',
                      gridTemplateColumns: 'minmax(200px, 1fr) 2fr',
                      gap: 12,
                      alignItems: 'center',
                      background: '#FFFFFF',
                      border: '1px solid #E2E8F0',
                      borderRadius: 10,
                      padding: '10px 14px',
                    }}
                  >
                    <div>
                      <strong style={{ color: '#0F172A', fontSize: '0.86rem' }}>
                        {payment.referenceNumber || payment.id}
                      </strong>
                      <span style={{ color: '#64748B', fontSize: '0.82rem', marginLeft: 8 }}>
                        ₱{payment.amount.toLocaleString()}
                      </span>
                    </div>
                    <input
                      value={exceptionReasons[payment.id] || ''}
                      onChange={(event) =>
                        setExceptionReasons((current) => ({ ...current, [payment.id]: event.target.value }))
                      }
                      placeholder="Reason to list this unresolved exception"
                      style={{
                        padding: '8px 12px',
                        border: '1.5px solid #CBD5E1',
                        borderRadius: 8,
                        fontSize: '0.85rem',
                        outline: 'none',
                      }}
                    />
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Compliance Prerequisite Notice */}
          <div className="pc-policy-banner">
            <i className="ti ti-info-circle-filled" style={{ fontSize: 20, color: '#0284C7', flexShrink: 0 }} />
            <div>
              <strong>Reconciliation Prerequisite:</strong> The close will be rejected unless all period payments
              are validated, liquidations are validated, AR equals aging, and the balances reconcile.
            </div>
          </div>

          {/* Action Row */}
          <div className="pc-actions-row">
            <Button
              title={saving ? 'Closing…' : 'Validate checklist and close'}
              variant="primary"
              size="md"
              icon="ti ti-lock"
              loading={saving}
              type="submit"
            />
          </div>
        </form>
      )}

      {/* ── Closed Periods Archive Card ── */}
      <section className="pc-card">
        <div className="pc-card-header">
          <div className="pc-card-title-group">
            <div className="pc-card-header-icon" style={{ background: '#F1F5F9', color: '#1B254B' }}>
              <i className="ti ti-history" />
            </div>
            <div>
              <h2 className="pc-card-title">Closed periods</h2>
              <p className="pc-card-subtitle">
                Historical sealed periods and certified dual-entry corrections.
              </p>
            </div>
          </div>

          <span
            style={{
              fontSize: '11px',
              fontWeight: 700,
              padding: '4px 10px',
              borderRadius: 12,
              background: '#F1F5F9',
              color: '#475569',
              textTransform: 'uppercase',
              letterSpacing: '0.5px',
            }}
          >
            {periods.length} Periods Archived
          </span>
        </div>

        {!periods.length ? (
          <div className="pc-empty-state">
            <div className="pc-empty-icon">
              <i className="ti ti-calendar-off" />
            </div>
            <h3 className="pc-empty-title">No periods have been closed.</h3>
            <p className="pc-empty-desc">
              When an accounting period is formally reconciled and closed by the Finance Manager, its verified ledger
              state and statement proofs will appear here.
            </p>
          </div>
        ) : (
          <div className="pc-period-list">
            {periods.map((period: AccountingPeriodClose) => {
              const isSelected = selectedPeriodId === period.id;
              return (
                <button
                  type="button"
                  key={period.id}
                  onClick={() => setSelectedPeriodId(isSelected ? '' : period.id)}
                  className={`pc-period-card ${isSelected ? 'is-selected' : ''}`}
                >
                  <div className="pc-period-meta-row">
                    <span className="pc-period-range">
                      <i className="ti ti-lock" style={{ marginRight: 6, color: '#00A99D' }} />
                      {period.periodType}: {period.periodStart.slice(0, 10)} – {period.periodEnd.slice(0, 10)}
                    </span>
                    <span
                      style={{
                        fontSize: '11px',
                        fontWeight: 700,
                        padding: '3px 8px',
                        borderRadius: 6,
                        background: isSelected ? '#00A99D' : '#F1F5F9',
                        color: isSelected ? '#FFFFFF' : '#475569',
                      }}
                    >
                      {isSelected ? 'Selected for Correction' : 'Locked & Audited'}
                    </span>
                  </div>

                  <div className="pc-period-details">
                    <span className="pc-period-detail-item">
                      <i className="ti ti-user-check" style={{ color: '#0D9488' }} />
                      Closed by: <strong>{period.closedBy}</strong>
                    </span>
                    <span className="pc-period-detail-item">
                      <i className="ti ti-credit-card" style={{ color: '#2563EB' }} />
                      Payments: <strong>{period.validatedPaymentCount}</strong>
                    </span>
                    <span className="pc-period-detail-item">
                      <i className="ti ti-receipt" style={{ color: '#D97706' }} />
                      Liquidations: <strong>{period.validatedLiquidationCount}</strong>
                    </span>
                    <span className="pc-period-detail-item">
                      <i className="ti ti-wallet" style={{ color: '#059669' }} />
                      AR Ledger: <strong>₱{period.receivableLedgerTotal.toLocaleString()}</strong>
                    </span>
                  </div>

                  <div style={{ color: '#64748B', fontSize: '12px', display: 'flex', alignItems: 'center', gap: 6, marginTop: 4 }}>
                    <i className="ti ti-file-certificate" style={{ color: '#94A3B8' }} />
                    Bank statement proof: <code>{period.bankStatementProofReference}</code>
                  </div>
                </button>
              );
            })}
          </div>
        )}

        {/* ── Correcting Entry Form ── */}
        {canClose && selectedPeriodId && selectedPeriod && (
          <form
            onSubmit={submitCorrection}
            style={{
              marginTop: 24,
              padding: 22,
              background: '#F8FAFC',
              border: '1.5px solid #CBD5E1',
              borderRadius: 14,
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 14 }}>
              <div>
                <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: '#0F172A', display: 'flex', alignItems: 'center', gap: 8 }}>
                  <i className="ti ti-scale" style={{ color: '#1B254B' }} />
                  Record a correcting entry for {selectedPeriod.periodType} ({selectedPeriod.periodStart.slice(0, 10)} to {selectedPeriod.periodEnd.slice(0, 10)})
                </h3>
                <p style={{ margin: '3px 0 0', fontSize: '0.82rem', color: '#64748B' }}>
                  Closed periods are locked. Subsequent adjustments must balance debit and credit accounts.
                </p>
              </div>
              <Button
                title="Cancel"
                variant="ghost"
                size="sm"
                icon="ti ti-x"
                onClick={() => setSelectedPeriodId('')}
              />
            </div>

            <div className="pc-form-grid" style={{ marginBottom: 16 }}>
              <div className="pc-field">
                <label className="pc-label" htmlFor="pc-corr-date">Entry date <span className="pc-label-req">*</span></label>
                <div className="pc-input-wrapper">
                  <div className="pc-input-prefix"><i className="ti ti-calendar" /></div>
                  <input
                    id="pc-corr-date"
                    type="date"
                    value={correctionDate}
                    onChange={(event) => setCorrectionDate(event.target.value)}
                    required
                    className="pc-input"
                  />
                </div>
              </div>

              <div className="pc-field">
                <label className="pc-label" htmlFor="pc-corr-desc">Description <span className="pc-label-req">*</span></label>
                <div className="pc-input-wrapper">
                  <div className="pc-input-prefix"><i className="ti ti-notes" /></div>
                  <input
                    id="pc-corr-desc"
                    value={correctionDescription}
                    onChange={(event) => setCorrectionDescription(event.target.value)}
                    required
                    placeholder="e.g. Bank reconciliation discrepancy adjustment"
                    className="pc-input"
                  />
                </div>
              </div>

              <div className="pc-field">
                <label className="pc-label" htmlFor="pc-corr-debit">Debit account <span className="pc-label-req">*</span></label>
                <div className="pc-input-wrapper">
                  <div className="pc-input-prefix"><i className="ti ti-arrow-up-right" /></div>
                  <input
                    id="pc-corr-debit"
                    value={debitAccount}
                    onChange={(event) => setDebitAccount(event.target.value)}
                    required
                    placeholder="e.g. Cash / Bank Clearing"
                    className="pc-input"
                  />
                </div>
              </div>

              <div className="pc-field">
                <label className="pc-label" htmlFor="pc-corr-credit">Credit account <span className="pc-label-req">*</span></label>
                <div className="pc-input-wrapper">
                  <div className="pc-input-prefix"><i className="ti ti-arrow-down-left" /></div>
                  <input
                    id="pc-corr-credit"
                    value={creditAccount}
                    onChange={(event) => setCreditAccount(event.target.value)}
                    required
                    placeholder="e.g. Accounts Receivable"
                    className="pc-input"
                  />
                </div>
              </div>

              <div className="pc-field">
                <label className="pc-label" htmlFor="pc-corr-amt">Balanced amount <span className="pc-label-req">*</span></label>
                <div className="pc-input-wrapper">
                  <div className="pc-input-prefix">₱</div>
                  <input
                    id="pc-corr-amt"
                    type="number"
                    min="0.01"
                    step="0.01"
                    value={correctionAmount}
                    onChange={(event) => setCorrectionAmount(event.target.value)}
                    required
                    placeholder="0.00"
                    className="pc-input"
                  />
                </div>
              </div>
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10 }}>
              <Button
                title={saving ? 'Saving…' : 'Record correction'}
                variant="navy"
                size="md"
                icon="ti ti-check"
                loading={saving}
                type="submit"
              />
            </div>
          </form>
        )}
      </section>
    </div>
  );
}
