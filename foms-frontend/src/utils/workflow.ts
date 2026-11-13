/**
 * Workflow config — ONE place for status labels, tones and next-action rules.
 *   Standard   : DRAFT -> SUBMITTED -> VERIFIED | RETURNED -> APPROVED | REJECTED
 *   Liquidation: SUBMITTED -> VALIDATED | RETURNED -> APPROVED
 *   Duplicate  : lives ONLY in ./duplicateWorkflow (FLAGGED -> PENDING_REVIEW -> APPROVED_DUPLICATE |
 *                CLEARED_NOT_DUPLICATE, or RETURNED). This file only re-exports it for labels/tones.
 */
import type { Role } from '../types/auth';
import { ROLES } from '../types/auth';
import type { Action } from './roleAccess';
import {
  DUPLICATE_STATUSES,
  DUPLICATE_STATUS_LABELS,
  DUPLICATE_STATUS_TONES,
  type DuplicateStatus,
} from './duplicateWorkflow';

export type WorkflowKind = 'standard' | 'liquidation';
export type WorkflowStatus =
  | 'DRAFT'
  | 'FOR_REVIEW'
  | 'SUBMITTED'
  | 'VERIFIED'
  | 'VALIDATED'
  | 'RETURNED'
  | 'RETURNED_FOR_REVISION'
  | 'PENDING_FINANCE_MANAGER'
  | 'PENDING_FM'
  | 'APPROVED'
  | 'REJECTED'
  | 'PENDING_HA_VERIFICATION'
  | 'PENDING_FM_APPROVAL'
  | 'RETURNED_TO_HA'
  | 'RETURNED_TO_CREATOR'
  | 'SENT'
  | 'RECEIVED'
  | 'DISPUTED'
  | 'VOID'
  // Cash Advance specific statuses (DMS-aligned mapping)
  | 'DISBURSED'
  | 'COMPLETED'
  | DuplicateStatus;

export type Tone = 'success' | 'warning' | 'danger' | 'info' | 'primary' | 'neutral';

export const STATUS_LABEL: Record<WorkflowStatus, string> = {
  DRAFT: 'Draft',
  FOR_REVIEW: 'Pending Approval',
  SUBMITTED: 'Submitted',
  VERIFIED: 'Verified',
  VALIDATED: 'Validated',
  APPROVED: 'Approved',
  REJECTED: 'Rejected',
  PENDING_FINANCE_MANAGER: 'Pending FM',
  PENDING_FM: 'Pending FM',
  RETURNED_FOR_REVISION: 'Returned',
  PENDING_HA_VERIFICATION: 'Pending HA Verification',
  PENDING_FM_APPROVAL: 'Pending FM Approval',
  RETURNED_TO_HA: 'Returned to HA',
  RETURNED_TO_CREATOR: 'Returned to Creator',
  SENT: 'Sent',
  RECEIVED: 'Received',
  DISPUTED: 'Disputed',
  VOID: 'Void',
  // Cash Advance: DMS-aligned labels
  DISBURSED: 'Disbursed',
  COMPLETED: 'Completed',
  ...DUPLICATE_STATUS_LABELS,
  RETURNED: 'Returned',
};

export const STATUS_TONE: Record<WorkflowStatus, Tone> = {
  DRAFT: 'neutral',
  FOR_REVIEW: 'warning',
  SUBMITTED: 'info',
  VERIFIED: 'primary',
  VALIDATED: 'primary',
  APPROVED: 'success',
  REJECTED: 'danger',
  PENDING_FINANCE_MANAGER: 'warning',
  PENDING_FM: 'warning',
  RETURNED_FOR_REVISION: 'danger',
  PENDING_HA_VERIFICATION: 'warning',
  PENDING_FM_APPROVAL: 'warning',
  RETURNED_TO_HA: 'danger',
  RETURNED_TO_CREATOR: 'danger',
  SENT: 'info',
  RECEIVED: 'success',
  DISPUTED: 'danger',
  VOID: 'neutral',
  // Cash Advance: DMS-aligned tones (Disbursed=blue/info, Completed=success)
  DISBURSED: 'info',
  COMPLETED: 'success',
  ...DUPLICATE_STATUS_TONES,
  RETURNED: 'danger',
};

/** status -> role -> actions allowed on a record in that status. */
const NEXT_ACTIONS: Record<WorkflowKind, Partial<Record<WorkflowStatus, Partial<Record<Role, readonly Action[]>>>>> = {
  standard: {
    DRAFT: { [ROLES.ACCOUNTANT]: ['edit', 'submit'] },
    RETURNED: { [ROLES.ACCOUNTANT]: ['edit', 'submit'] },
    SUBMITTED: { [ROLES.HEAD_ACCOUNTANT]: ['verify', 'return'] },
    VERIFIED: { [ROLES.FINANCE_MANAGER]: ['approve', 'reject'] },
  },
  liquidation: {
    SUBMITTED: { [ROLES.ASSISTANT_FM]: ['verify', 'return'] },
    VALIDATED: { [ROLES.FINANCE_MANAGER]: ['approve', 'reject'] },
  },
};

/** Actions this role may take on a record in this status (before module-level permission is applied). */
export function nextActions(kind: WorkflowKind, status: string, role: Role | null | undefined): readonly Action[] {
  if (!role) return [];
  return NEXT_ACTIONS[kind][normalizeStatus(status)]?.[role] ?? [];
}

const KNOWN = new Set<string>([...Object.keys(STATUS_LABEL), ...DUPLICATE_STATUSES]);

/** Accepts API status text in any casing/spelling and returns canonical status (unknown values pass through). */
export function normalizeStatus(raw: string | null | undefined): WorkflowStatus {
  const s = String(raw ?? '')
    .trim()
    .replace(/([a-z])([A-Z])/g, '$1_$2')
    .toUpperCase()
    .replace(/[-\s]+/g, '_');
  if (s === 'PENDING_FINANCE_MANAGER' || s === 'PENDINGFINANCEMANAGER') return 'PENDING_FINANCE_MANAGER';
  if (s === 'RETURNED_FOR_REVISION' || s === 'RETURNEDFORREVISION') return 'RETURNED_FOR_REVISION';
  if (s === 'PENDING_HA_VERIFICATION' || s === 'PENDINGHAVERIFICATION') return 'PENDING_HA_VERIFICATION';
  if (s === 'PENDING_FM_APPROVAL' || s === 'PENDINGFMAPPROVAL') return 'PENDING_FM_APPROVAL';
  if (s === 'RETURNED_TO_HA' || s === 'RETURNEDTOHA') return 'RETURNED_TO_HA';
  if (s === 'RETURNED_TO_CREATOR' || s === 'RETURNEDTOCREATOR') return 'RETURNED_TO_CREATOR';
  if (s === 'SENT') return 'SENT';
  if (s === 'RECEIVED') return 'RECEIVED';
  if (s === 'DISPUTED') return 'DISPUTED';
  if (s === 'VOID' || s === 'VOIDED') return 'VOID';
  if (s === 'DISBURSED') return 'DISBURSED';
  if (s === 'COMPLETED') return 'COMPLETED';
  return (s || 'DRAFT') as WorkflowStatus;
}

export function isKnownStatus(raw: string | null | undefined): boolean {
  return KNOWN.has(normalizeStatus(raw));
}

export {
  normalizeDuplicateStatus,
  canPerformDuplicateAction,
  canDecideDuplicate,
  canReviewDuplicate,
  canApproveOwnCheck,
  getNextDuplicateStatus,
  DUPLICATE_STATUSES,
  DUPLICATE_STATUS_LABELS,
  DUPLICATE_STATUS_TONES,
  type DuplicateStatus,
  type Level1Decision,
  type Level2Action,
} from './duplicateWorkflow';

/** Workflow label for a known status; unknown API values are shown verbatim (never crash). */
export function statusLabel(raw: string | null | undefined): string {
  const key = normalizeStatus(raw);
  return STATUS_LABEL[key] ?? String(raw ?? '');
}
