import { http } from './apiClient';

export interface AccountingPeriodClose {
  id: string;
  periodType: 'Month' | 'Year';
  periodStart: string;
  periodEnd: string;
  closedBy: string;
  closedAt: string;
  validatedPaymentCount: number;
  validatedLiquidationCount: number;
  receivableLedgerTotal: number;
  agingReportTotal: number;
  bankBookBalance: number;
  bankStatementBalance: number;
  bankStatementProofReference: string;
  cashBookBalance: number;
  countedCashBalance: number;
  listedExceptionsJson: string;
}

export interface ClosePeriodPayload {
  periodType: 'Month' | 'Year';
  periodStart: string;
  periodEnd: string;
  bankBookBalance: number;
  bankStatementBalance: number;
  bankStatementProofReference: string;
  cashBookBalance: number;
  countedCashBalance: number;
  listedExceptions: Array<{ paymentId: string; reason: string }>;
}

export interface CorrectionEntryPayload {
  entryDate: string;
  description: string;
  debitAccount: string;
  creditAccount: string;
  amount: number;
}

export const periodCloseService = {
  list: () => http.get<AccountingPeriodClose[]>('/finance/period-closes'),
  close: (payload: ClosePeriodPayload) => http.post<AccountingPeriodClose>('/finance/period-closes', payload),
  addCorrection: (periodCloseId: string, payload: CorrectionEntryPayload) =>
    http.post(`/finance/period-closes/${encodeURIComponent(periodCloseId)}/corrections`, payload),
};
