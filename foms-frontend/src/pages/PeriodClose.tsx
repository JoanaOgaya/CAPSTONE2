import { useMemo, useState, type FormEvent } from 'react';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../components/ToastContext';
import { useQuery } from '../hooks/useQuery';
import { paymentsService } from '../services/paymentsService';
import { periodCloseService, type AccountingPeriodClose } from '../services/periodCloseService';
import { Role } from '../types/auth';
import type { Payment } from '../types/entities';
import { formatCurrency } from '../utils/format';

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

  const inputStyle = {
    width: '100%',
    boxSizing: 'border-box' as const,
    padding: '10px 14px',
    border: '1px solid #CBD5E1',
    borderRadius: '8px',
    fontSize: '13.5px',
    color: '#1E293B',
    background: '#F8FAFC',
    outline: 'none',
    transition: 'all 0.15s ease',
  };

  const labelStyle = {
    display: 'flex',
    flexDirection: 'column' as const,
    gap: '6px',
    fontSize: '13px',
    fontWeight: 600,
    color: '#334155',
  };

  return (
    <div style={{ width: '100%', display: 'flex', flexDirection: 'column', gap: '24px', paddingBottom: '40px' }}>
      {/* ─── Page Header ─── */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px' }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <h1 style={{ margin: 0, fontSize: '24px', fontWeight: 800, color: '#0F172A', letterSpacing: '-0.02em' }}>
              Period Close
            </h1>
            <span
              style={{
                background: '#F1F5F9',
                color: '#475569',
                border: '1px solid #E2E8F0',
                padding: '3px 10px',
                borderRadius: '20px',
                fontSize: '11px',
                fontWeight: 700,
                textTransform: 'uppercase',
                letterSpacing: '0.04em',
              }}
            >
              Fiscal Governance
            </span>
          </div>
          <p style={{ margin: '6px 0 0', fontSize: '14px', color: '#64748B' }}>
            Complete the monthly or yearly reconciliation checklist. Closed periods are locked; later corrections must be recorded as balanced entries.
          </p>
        </div>
      </div>

      {periodError && (
        <div
          role="alert"
          style={{
            background: '#FEF2F2',
            border: '1px solid #FECACA',
            borderRadius: '12px',
            padding: '14px 18px',
            color: '#B91C1C',
            display: 'flex',
            alignItems: 'center',
            gap: '10px',
            fontSize: '13.5px',
          }}
        >
          <i className="ti ti-alert-triangle" style={{ fontSize: '18px' }} />
          <span>{periodError.message}</span>
        </div>
      )}

      {/* ─── Close Period Form Card ─── */}
      {canClose && (
        <form
          onSubmit={submitClose}
          style={{
            background: '#FFFFFF',
            border: '1px solid #E2E8F0',
            borderRadius: '16px',
            padding: '24px 28px',
            boxShadow: '0 1px 3px rgba(15, 23, 42, 0.04)',
            display: 'flex',
            flexDirection: 'column',
            gap: '24px',
          }}
        >
          {/* Section 1: Period Selection */}
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '16px', borderBottom: '1px solid #F1F5F9', paddingBottom: '12px' }}>
              <div
                style={{
                  width: '32px',
                  height: '32px',
                  borderRadius: '8px',
                  background: '#EFF6FF',
                  color: '#2563EB',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: '16px',
                }}
              >
                <i className="ti ti-calendar" />
              </div>
              <div>
                <h3 style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: '#0F172A' }}>
                  1. Target Accounting Period
                </h3>
                <p style={{ margin: '2px 0 0', fontSize: '12px', color: '#64748B' }}>
                  Select the period frequency and timeline to reconcile and lock
                </p>
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '16px' }}>
              <label style={labelStyle}>
                <span>Period Type <strong style={{ color: '#EF4444' }}>*</strong></span>
                <select
                  value={periodType}
                  onChange={(event) => {
                    const type = event.target.value as 'Month' | 'Year';
                    setPeriodType(type);
                    setPeriodValue((current) => (type === 'Year' ? current.slice(0, 4) : `${current.slice(0, 4)}-01`));
                  }}
                  style={inputStyle}
                >
                  <option value="Month">Monthly Close</option>
                  <option value="Year">Year-End Annual Close</option>
                </select>
              </label>

              <label style={labelStyle}>
                <span>{periodType === 'Month' ? 'Select Month' : 'Select Year'} <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  type={periodType === 'Month' ? 'month' : 'number'}
                  min={periodType === 'Year' ? 2000 : undefined}
                  max={periodType === 'Year' ? 9999 : undefined}
                  value={periodValue}
                  onChange={(event) => setPeriodValue(event.target.value)}
                  required
                  style={inputStyle}
                />
              </label>

              <div style={{ ...labelStyle, justifyContent: 'center' }}>
                <span style={{ fontSize: '12px', color: '#64748B' }}>Active Bounds:</span>
                <div
                  style={{
                    padding: '9px 12px',
                    borderRadius: '8px',
                    background: '#F1F5F9',
                    border: '1px solid #E2E8F0',
                    fontSize: '13px',
                    fontWeight: 700,
                    color: '#334155',
                    fontFamily: 'ui-monospace, monospace',
                  }}
                >
                  {periodBounds.start} &rarr; {periodBounds.end}
                </div>
              </div>
            </div>
          </div>

          {/* Section 2: Bank Reconciliation */}
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '16px', borderBottom: '1px solid #F1F5F9', paddingBottom: '12px' }}>
              <div
                style={{
                  width: '32px',
                  height: '32px',
                  borderRadius: '8px',
                  background: '#F0FDFA',
                  color: '#0D9488',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: '16px',
                }}
              >
                <i className="ti ti-building-bank" />
              </div>
              <div>
                <h3 style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: '#0F172A' }}>
                  2. Bank Reconciliation
                </h3>
                <p style={{ margin: '2px 0 0', fontSize: '12px', color: '#64748B' }}>
                  Enter general ledger book balance and verified bank statement balances
                </p>
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '16px' }}>
              <label style={labelStyle}>
                <span>Bank Book Balance (PHP) <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  type="number"
                  min="0"
                  step="0.01"
                  value={bankBookBalance}
                  onChange={(event) => setBankBookBalance(event.target.value)}
                  required
                  style={inputStyle}
                  placeholder="0.00"
                />
              </label>

              <label style={labelStyle}>
                <span>Bank Statement Closing Balance <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  type="number"
                  min="0"
                  step="0.01"
                  value={bankStatementBalance}
                  onChange={(event) => setBankStatementBalance(event.target.value)}
                  required
                  style={inputStyle}
                  placeholder="0.00"
                />
              </label>

              <label style={labelStyle}>
                <span>Statement Proof Reference ID <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  value={bankStatementProofReference}
                  onChange={(event) => setBankStatementProofReference(event.target.value)}
                  required
                  style={inputStyle}
                  placeholder="e.g. DOC-BS-2026-10 or secure document path"
                />
              </label>
            </div>
          </div>

          {/* Section 3: Cash Reconciliation */}
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '16px', borderBottom: '1px solid #F1F5F9', paddingBottom: '12px' }}>
              <div
                style={{
                  width: '32px',
                  height: '32px',
                  borderRadius: '8px',
                  background: '#FEF3C7',
                  color: '#D97706',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: '16px',
                }}
              >
                <i className="ti ti-cash" />
              </div>
              <div>
                <h3 style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: '#0F172A' }}>
                  3. Cash on Hand Reconciliation
                </h3>
                <p style={{ margin: '2px 0 0', fontSize: '12px', color: '#64748B' }}>
                  Reconcile petty cash ledger balance against audited physical cash count
                </p>
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '16px' }}>
              <label style={labelStyle}>
                <span>Cash Book Balance (PHP) <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  type="number"
                  min="0"
                  step="0.01"
                  value={cashBookBalance}
                  onChange={(event) => setCashBookBalance(event.target.value)}
                  required
                  style={inputStyle}
                  placeholder="0.00"
                />
              </label>

              <label style={labelStyle}>
                <span>Counted Physical Cash Balance <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  type="number"
                  min="0"
                  step="0.01"
                  value={countedCashBalance}
                  onChange={(event) => setCountedCashBalance(event.target.value)}
                  required
                  style={inputStyle}
                  placeholder="0.00"
                />
              </label>
            </div>
          </div>

          {/* Section 4: Unresolved Exceptions */}
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '14px', borderBottom: '1px solid #F1F5F9', paddingBottom: '12px' }}>
              <div
                style={{
                  width: '32px',
                  height: '32px',
                  borderRadius: '8px',
                  background: periodExceptions.length === 0 ? '#ECFDF5' : '#FEF2F2',
                  color: periodExceptions.length === 0 ? '#059669' : '#DC2626',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: '16px',
                }}
              >
                <i className={periodExceptions.length === 0 ? 'ti ti-check' : 'ti ti-alert-triangle'} />
              </div>
              <div>
                <h3 style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: '#0F172A' }}>
                  4. Payment Exceptions Audit
                </h3>
                <p style={{ margin: '2px 0 0', fontSize: '12px', color: '#64748B' }}>
                  Review open payment exceptions recorded within this period boundary
                </p>
              </div>
            </div>

            {periodExceptions.length === 0 ? (
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '12px',
                  background: '#F0FDF4',
                  border: '1px solid #BBF7D0',
                  borderRadius: '10px',
                  padding: '14px 18px',
                  color: '#166534',
                  fontSize: '13.5px',
                }}
              >
                <i className="ti ti-circle-check" style={{ fontSize: '20px', color: '#16A34A', flexShrink: 0 }} />
                <span>
                  <strong>Clean Audit:</strong> No unresolved payment exceptions are recorded for this period.
                </span>
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                {periodExceptions.map((payment) => (
                  <div
                    key={payment.id}
                    style={{
                      display: 'grid',
                      gridTemplateColumns: 'minmax(220px, 1fr) 2fr',
                      gap: '14px',
                      alignItems: 'center',
                      background: '#FFFBEB',
                      border: '1px solid #FDE68A',
                      borderRadius: '8px',
                      padding: '12px 16px',
                    }}
                  >
                    <div>
                      <div style={{ fontWeight: 700, color: '#92400E', fontSize: '13.5px', fontFamily: 'monospace' }}>
                        {payment.referenceNumber || payment.id}
                      </div>
                      <div style={{ fontSize: '12px', color: '#B45309' }}>Amount: {formatCurrency(payment.amount)}</div>
                    </div>
                    <input
                      value={exceptionReasons[payment.id] || ''}
                      onChange={(event) =>
                        setExceptionReasons((current) => ({ ...current, [payment.id]: event.target.value }))
                      }
                      style={inputStyle}
                      placeholder="Reason for listing this unresolved exception..."
                    />
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Validation Notice Banner */}
          <div
            style={{
              background: '#EFF6FF',
              border: '1px solid #BFDBFE',
              borderRadius: '12px',
              padding: '14px 18px',
              display: 'flex',
              alignItems: 'flex-start',
              gap: '12px',
              color: '#1E40AF',
              fontSize: '13px',
              lineHeight: '1.5',
            }}
          >
            <i className="ti ti-info-circle" style={{ fontSize: '20px', color: '#2563EB', flexShrink: 0, marginTop: '2px' }} />
            <div>
              <strong>Reconciliation Validation Rule:</strong> The close will be rejected unless all period customer payments are verified, liquidations are validated, AR ledger balances match aging totals, and cash and bank accounts balance exactly.
            </div>
          </div>

          {/* Submit Action Button */}
          <div style={{ display: 'flex', justifyContent: 'flex-end', paddingTop: '8px' }}>
            <button
              type="submit"
              disabled={saving}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '8px',
                padding: '12px 24px',
                background: '#0D9488',
                color: '#FFFFFF',
                border: 'none',
                borderRadius: '10px',
                fontSize: '14px',
                fontWeight: 700,
                cursor: saving ? 'not-allowed' : 'pointer',
                boxShadow: '0 2px 6px rgba(13, 148, 136, 0.25)',
                transition: 'all 0.15s ease',
              }}
              onMouseEnter={(e) => !saving && (e.currentTarget.style.background = '#0F766E')}
              onMouseLeave={(e) => !saving && (e.currentTarget.style.background = '#0D9488')}
            >
              <i className={saving ? 'ti ti-loader-2 animate-spin' : 'ti ti-lock'} style={{ fontSize: '16px' }} />
              {saving ? 'Closing & Auditing Period…' : 'Validate Checklist & Lock Period'}
            </button>
          </div>
        </form>
      )}

      {/* ─── Closed Periods History ─── */}
      <section
        style={{
          background: '#FFFFFF',
          border: '1px solid #E2E8F0',
          borderRadius: '16px',
          padding: '24px 28px',
          boxShadow: '0 1px 3px rgba(15, 23, 42, 0.04)',
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '18px' }}>
          <div>
            <h2 style={{ margin: 0, fontSize: '18px', fontWeight: 800, color: '#0F172A' }}>
              Historical Closed Periods
            </h2>
            <p style={{ margin: '4px 0 0', fontSize: '13px', color: '#64748B' }}>
              Audited and locked fiscal periods. Select a period to view details or post balanced adjusting entries.
            </p>
          </div>
          <span
            style={{
              background: '#F1F5F9',
              color: '#334155',
              padding: '4px 12px',
              borderRadius: '20px',
              fontSize: '12px',
              fontWeight: 700,
            }}
          >
            {periods.length} closed {periods.length === 1 ? 'record' : 'records'}
          </span>
        </div>

        {!periods.length ? (
          <div
            style={{
              padding: '48px 24px',
              textAlign: 'center',
              background: '#F8FAFC',
              borderRadius: '12px',
              border: '1px dashed #CBD5E1',
            }}
          >
            <i className="ti ti-folder-off" style={{ fontSize: '36px', color: '#94A3B8', display: 'block', marginBottom: '8px' }} />
            <h4 style={{ margin: 0, fontSize: '15px', color: '#475569' }}>No periods have been closed yet</h4>
            <p style={{ margin: '4px 0 0', fontSize: '13px', color: '#94A3B8' }}>
              When a fiscal month or year is locked, it will be listed here.
            </p>
          </div>
        ) : (
          <div style={{ display: 'grid', gap: '12px' }}>
            {periods.map((period: AccountingPeriodClose) => {
              const isSelected = selectedPeriodId === period.id;
              return (
                <div
                  key={period.id}
                  onClick={() => setSelectedPeriodId(isSelected ? '' : period.id)}
                  style={{
                    background: isSelected ? '#F0FDFA' : '#FFFFFF',
                    border: isSelected ? '1.5px solid #0D9488' : '1px solid #E2E8F0',
                    borderRadius: '12px',
                    padding: '16px 20px',
                    cursor: 'pointer',
                    transition: 'all 0.2s ease',
                    boxShadow: isSelected ? '0 4px 12px rgba(13, 148, 136, 0.08)' : '0 1px 2px rgba(0, 0, 0, 0.02)',
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '10px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                      <span
                        style={{
                          background: period.periodType === 'Year' ? '#FEF3C7' : '#EFF6FF',
                          color: period.periodType === 'Year' ? '#92400E' : '#1E40AF',
                          padding: '3px 10px',
                          borderRadius: '20px',
                          fontSize: '11px',
                          fontWeight: 800,
                          textTransform: 'uppercase',
                        }}
                      >
                        {period.periodType}
                      </span>
                      <strong style={{ fontSize: '15px', color: '#0F172A' }}>
                        {period.periodStart.slice(0, 10)} &rarr; {period.periodEnd.slice(0, 10)}
                      </strong>
                    </div>

                    <span
                      style={{
                        background: '#ECFDF5',
                        color: '#047857',
                        border: '1px solid #A7F3D0',
                        padding: '3px 10px',
                        borderRadius: '20px',
                        fontSize: '11px',
                        fontWeight: 700,
                        display: 'inline-flex',
                        alignItems: 'center',
                        gap: '4px',
                      }}
                    >
                      <i className="ti ti-lock" style={{ fontSize: '11px' }} /> Locked & Audited
                    </span>
                  </div>

                  <div
                    style={{
                      display: 'grid',
                      gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
                      gap: '12px',
                      marginTop: '12px',
                      paddingTop: '12px',
                      borderTop: '1px solid #F1F5F9',
                      fontSize: '12.5px',
                    }}
                  >
                    <div>
                      <span style={{ color: '#64748B' }}>Closed By: </span>
                      <strong style={{ color: '#334155' }}>{period.closedBy}</strong>
                    </div>
                    <div>
                      <span style={{ color: '#64748B' }}>Validated Payments: </span>
                      <strong style={{ color: '#334155' }}>{period.validatedPaymentCount}</strong>
                    </div>
                    <div>
                      <span style={{ color: '#64748B' }}>Validated Liquidations: </span>
                      <strong style={{ color: '#334155' }}>{period.validatedLiquidationCount}</strong>
                    </div>
                    <div>
                      <span style={{ color: '#64748B' }}>AR Ledger Total: </span>
                      <strong style={{ color: '#0D9488' }}>{formatCurrency(period.receivableLedgerTotal)}</strong>
                    </div>
                  </div>

                  <div style={{ marginTop: '8px', fontSize: '12px', color: '#64748B', display: 'flex', alignItems: 'center', gap: '6px' }}>
                    <i className="ti ti-file-certificate" style={{ color: '#94A3B8' }} />
                    <span>Proof Reference: <strong style={{ color: '#475569' }}>{period.bankStatementProofReference}</strong></span>
                  </div>
                </div>
              );
            })}
          </div>
        )}

        {/* ─── Record Correcting Entry (when period is selected) ─── */}
        {canClose && selectedPeriodId && (
          <form
            onSubmit={submitCorrection}
            style={{
              marginTop: '24px',
              paddingTop: '20px',
              borderTop: '1px solid #E2E8F0',
              display: 'flex',
              flexDirection: 'column',
              gap: '16px',
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <i className="ti ti-adjustments-horizontal" style={{ fontSize: '18px', color: '#2563EB' }} />
              <h3 style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: '#0F172A' }}>
                Post Balanced Adjusting Entry
              </h3>
            </div>
            <p style={{ margin: 0, fontSize: '13px', color: '#64748B' }}>
              All adjustments to locked periods must strictly follow double-entry bookkeeping with equal debits and credits.
            </p>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '14px' }}>
              <label style={labelStyle}>
                <span>Entry Date <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  type="date"
                  value={correctionDate}
                  onChange={(event) => setCorrectionDate(event.target.value)}
                  required
                  style={inputStyle}
                />
              </label>

              <label style={labelStyle}>
                <span>Description <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  value={correctionDescription}
                  onChange={(event) => setCorrectionDescription(event.target.value)}
                  required
                  style={inputStyle}
                  placeholder="e.g. Bank charge adjustment"
                />
              </label>

              <label style={labelStyle}>
                <span>Debit Account <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  value={debitAccount}
                  onChange={(event) => setDebitAccount(event.target.value)}
                  required
                  style={inputStyle}
                  placeholder="e.g. 5010 Bank Expense"
                />
              </label>

              <label style={labelStyle}>
                <span>Credit Account <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  value={creditAccount}
                  onChange={(event) => setCreditAccount(event.target.value)}
                  required
                  style={inputStyle}
                  placeholder="e.g. 1010 Cash in Bank"
                />
              </label>

              <label style={labelStyle}>
                <span>Balanced Amount (PHP) <strong style={{ color: '#EF4444' }}>*</strong></span>
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  value={correctionAmount}
                  onChange={(event) => setCorrectionAmount(event.target.value)}
                  required
                  style={inputStyle}
                  placeholder="0.00"
                />
              </label>
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '8px' }}>
              <button
                type="submit"
                disabled={saving}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '8px',
                  padding: '10px 20px',
                  background: '#1E293B',
                  color: '#FFFFFF',
                  border: 'none',
                  borderRadius: '8px',
                  fontWeight: 700,
                  fontSize: '13.5px',
                  cursor: saving ? 'not-allowed' : 'pointer',
                  transition: 'background 0.15s ease',
                }}
                onMouseEnter={(e) => !saving && (e.currentTarget.style.background = '#0F172A')}
                onMouseLeave={(e) => !saving && (e.currentTarget.style.background = '#1E293B')}
              >
                <i className={saving ? 'ti ti-loader-2 animate-spin' : 'ti ti-check'} style={{ fontSize: '15px' }} />
                {saving ? 'Posting Entry…' : 'Record Correcting Entry'}
              </button>
            </div>
          </form>
        )}
      </section>
    </div>
  );
}
