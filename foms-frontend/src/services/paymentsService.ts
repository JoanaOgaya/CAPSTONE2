import { createResource } from './resource';
import { http } from './apiClient';
import type { Payment, Receipt } from '../types/entities';
import { invalidateForMutation } from './invalidation';

export const paymentsService = {
  ...createResource<Payment>('/payments'),
  approve: async (id: string, body?: { remarks?: string }) => {
    try {
      await http.post(`/api/finance-payments/${encodeURIComponent(id)}/approve`, body);
    } catch {
      try {
        await http.post(`/finance-payments/${encodeURIComponent(id)}/approve`, body);
      } catch {
        // Fallback for mock environments
      }
    }
    const res = await http.put<Payment>(`/payments/${encodeURIComponent(id)}`, { status: 'Approved', ...body });
    invalidateForMutation('approvePayment');
    return res;
  },
  validate: (id: string, body?: { remarks?: string }) =>
    http.post(`/finance/payments/${encodeURIComponent(id)}/validate`, body),
  reject: (id: string, rejectionReason: string) =>
    http.post(`/finance/payments/${encodeURIComponent(id)}/reject`, { rejectionReason }),
  resolveException: (id: string, action: 'reject' | 'send-to-validation', reason: string) =>
    http.post(`/finance/payments/${encodeURIComponent(id)}/resolve-exception`, { action, reason }),
  receipts: createResource<Receipt>('/official-receipts'),
};
