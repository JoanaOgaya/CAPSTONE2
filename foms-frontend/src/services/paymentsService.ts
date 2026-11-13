import { createResource } from './resource';
import { http } from './apiClient';
import type { Payment, Receipt } from '../types/entities';

export const paymentsService = {
  ...createResource<Payment>('/payments'),
  validate: (id: string, body?: { remarks?: string }) =>
    http.post(`/finance/payments/${encodeURIComponent(id)}/validate`, body),
  reject: (id: string, rejectionReason: string) =>
    http.post(`/finance/payments/${encodeURIComponent(id)}/reject`, { rejectionReason }),
  resolveException: (id: string, action: 'reject' | 'send-to-validation', reason: string) =>
    http.post(`/finance/payments/${encodeURIComponent(id)}/resolve-exception`, { action, reason }),
  receipts: createResource<Receipt>('/official-receipts'),
};
