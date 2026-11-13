import { useMemo, useState, type FormEvent } from 'react';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../components/ToastContext';
import { useQuery } from '../hooks/useQuery';
import { paymentsService } from '../services/paymentsService';
import { periodCloseService, type AccountingPeriodClose } from '../services/periodCloseService';
import { Role } from '../types/auth';
import type { Payment } from '../types/entities';

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
    padding: '9px 10px',
    border: '1px solid #CBD5E1',
    borderRadius: 7,
    font: 'inherit',
  };

  return (
    <div style={{ maxWidth: 1120, margin: '0 auto', padding: 24 }}>
      <header style={{ marginBottom: 20 }}>
        <h1 style={{ margin: 0, color: '#0F172A' }}>Period Close</h1>
        <p style={{ color: '#64748B', margin: '6px 0 0' }}>
          Complete the monthly or yearly checklist. Closed periods are locked; later corrections must be recorded as balanced entries.
        </p>
      </header>

      {periodError && <p role="alert" style={{ color: '#B91C1C' }}>{periodError.message}</p>}

      {canClose && (
        <form onSubmit={submitClose} style={{ background: '#fff', border: '1px solid #E2E8F0', borderRadius: 12, padding: 20, marginBottom: 24 }}>
          <h2 style={{ marginTop: 0 }}>Close a period</h2>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 14 }}>
            <label>
              Period type
              <select
                value={periodType}
                onChange={(event) => {
                  const type = event.target.value as 'Month' | 'Year';
                  setPeriodType(type);
                  setPeriodValue((current) => type === 'Year' ? current.slice(0, 4) : `${current.slice(0, 4)}-01`);
                }}
                style={inputStyle}
              >
                <option value="Month">Month</option>
                <option value="Year">Year</option>
              </select>
            </label>
            <label>
              {periodType === 'Month' ? 'Month' : 'Year'}
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
            <label>
              Bank book balance
              <input type="number" min="0" step="0.01" value={bankBookBalance} onChange={(event) => setBankBookBalance(event.target.value)} required style={inputStyle} />
            </label>
            <label>
              Bank statement closing balance
              <input type="number" min="0" step="0.01" value={bankStatementBalance} onChange={(event) => setBankStatementBalance(event.target.value)} required style={inputStyle} />
            </label>
            <label>
              Bank statement proof reference
              <input value={bankStatementProofReference} onChange={(event) => setBankStatementProofReference(event.target.value)} required style={inputStyle} placeholder="Stored document ID or secure path" />
            </label>
            <label>
              Cash book balance
              <input type="number" min="0" step="0.01" value={cashBookBalance} onChange={(event) => setCashBookBalance(event.target.value)} required style={inputStyle} />
            </label>
            <label>
              Counted cash balance
              <input type="number" min="0" step="0.01" value={countedCashBalance} onChange={(event) => setCountedCashBalance(event.target.value)} required style={inputStyle} />
            </label>
          </div>

          <section style={{ marginTop: 18 }}>
            <h3 style={{ marginBottom: 8 }}>Unresolved payment exceptions</h3>
            {periodExceptions.length === 0 ? (
              <p style={{ color: '#64748B' }}>No payment exceptions are recorded for this period.</p>
            ) : periodExceptions.map((payment) => (
              <label key={payment.id} style={{ display: 'grid', gridTemplateColumns: 'minmax(160px, 1fr) 2fr', gap: 10, alignItems: 'center', marginBottom: 8 }}>
                <span>{payment.referenceNumber || payment.id} · {payment.amount.toLocaleString()}</span>
                <input
                  value={exceptionReasons[payment.id] || ''}
                  onChange={(event) => setExceptionReasons((current) => ({ ...current, [payment.id]: event.target.value }))}
                  style={inputStyle}
                  placeholder="Reason to list this unresolved exception"
                />
              </label>
            ))}
          </section>

          <p style={{ color: '#64748B', fontSize: 13 }}>
            The close will be rejected unless all period payments are validated, liquidations are validated, AR equals aging, and the balances reconcile.
          </p>
          <button type="submit" disabled={saving} style={{ padding: '10px 16px', background: '#0D9488', color: '#fff', border: 0, borderRadius: 8, fontWeight: 700 }}>
            {saving ? 'Closing…' : 'Validate checklist and close'}
          </button>
        </form>
      )}

      <section style={{ background: '#fff', border: '1px solid #E2E8F0', borderRadius: 12, padding: 20 }}>
        <h2 style={{ marginTop: 0 }}>Closed periods</h2>
        {!periods.length ? <p style={{ color: '#64748B' }}>No periods have been closed.</p> : (
          <div style={{ display: 'grid', gap: 10 }}>
            {periods.map((period: AccountingPeriodClose) => (
              <button
                type="button"
                key={period.id}
                onClick={() => setSelectedPeriodId(period.id)}
                style={{ textAlign: 'left', background: selectedPeriodId === period.id ? '#F0FDFA' : '#fff', border: '1px solid #CBD5E1', borderRadius: 8, padding: 12, cursor: 'pointer' }}
              >
                <strong>{period.periodType}: {period.periodStart.slice(0, 10)} – {period.periodEnd.slice(0, 10)}</strong>
                <span style={{ display: 'block', marginTop: 4, color: '#475569', fontSize: 13 }}>
                  Closed by {period.closedBy} · Payments {period.validatedPaymentCount} · Liquidations {period.validatedLiquidationCount} · AR {period.receivableLedgerTotal.toLocaleString()}
                </span>
                <span style={{ display: 'block', color: '#64748B', fontSize: 12 }}>
                  Bank statement proof: {period.bankStatementProofReference}
                </span>
              </button>
            ))}
          </div>
        )}

        {canClose && selectedPeriodId && (
          <form onSubmit={submitCorrection} style={{ marginTop: 20, paddingTop: 16, borderTop: '1px solid #E2E8F0' }}>
            <h3>Record a correcting entry</h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 10 }}>
              <label>Entry date<input type="date" value={correctionDate} onChange={(event) => setCorrectionDate(event.target.value)} required style={inputStyle} /></label>
              <label>Description<input value={correctionDescription} onChange={(event) => setCorrectionDescription(event.target.value)} required style={inputStyle} /></label>
              <label>Debit account<input value={debitAccount} onChange={(event) => setDebitAccount(event.target.value)} required style={inputStyle} /></label>
              <label>Credit account<input value={creditAccount} onChange={(event) => setCreditAccount(event.target.value)} required style={inputStyle} /></label>
              <label>Balanced amount<input type="number" min="0.01" step="0.01" value={correctionAmount} onChange={(event) => setCorrectionAmount(event.target.value)} required style={inputStyle} /></label>
            </div>
            <button type="submit" disabled={saving} style={{ marginTop: 12, padding: '9px 14px', background: '#334155', color: '#fff', border: 0, borderRadius: 8, fontWeight: 700 }}>
              {saving ? 'Saving…' : 'Record correction'}
            </button>
          </form>
        )}
      </section>
    </div>
  );
}
