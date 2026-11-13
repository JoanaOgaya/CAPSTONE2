/**
 * src/__tests__/uiPolish.test.ts
 * UI Polish checklist smoke tests (Items 1, 4, 5).
 *
 * Item 1  — HTML title in index.html
 * Item 4  — formatCurrency: positive, zero, negative, large number, two decimals
 * Item 5  — Cash Advance status → badge tone via statusTone + normalizeStatus
 */

import { formatCurrency } from '../utils/format';
import { statusTone } from '../utils/status';
import { normalizeStatus } from '../utils/workflow';
import * as fs from 'fs';
import * as path from 'path';

// ─── Item 1: HTML Title ───────────────────────────────────────────────────────
describe('Item 1 — HTML title', () => {
  it('index.html <title> matches the required value', () => {
    // Read relative to the project root (foms-frontend)
    const htmlPath = path.resolve(__dirname, '../../index.html');
    const html = fs.readFileSync(htmlPath, 'utf-8');
    // em dash is U+2014; &amp; must be used in HTML
    expect(html).toContain('<title>FOMS \u2014 Speedex Finance &amp; Operations Management System</title>');
  });
});

// ─── Item 4: formatCurrency ───────────────────────────────────────────────────
describe('Item 4 — formatCurrency', () => {
  it('formats a positive integer with peso sign, space, comma separator and 2 decimals', () => {
    expect(formatCurrency(5000)).toBe('\u20b1 5,000.00');
  });

  it('formats zero as ₱ 0.00', () => {
    expect(formatCurrency(0)).toBe('\u20b1 0.00');
  });

  it('formats a negative amount with leading minus', () => {
    const result = formatCurrency(-1500);
    expect(result).toBe('-\u20b1 1,500.00');
  });

  it('formats a large number with comma thousands separators', () => {
    expect(formatCurrency(1234567.89)).toBe('\u20b1 1,234,567.89');
  });

  it('always returns exactly two decimal places', () => {
    expect(formatCurrency(100)).toMatch(/\.\d{2}$/);
    expect(formatCurrency(99.9)).toMatch(/\.\d{2}$/);
    expect(formatCurrency(0.005)).toMatch(/\.\d{2}$/);
  });

  it('returns N/A for null', () => {
    expect(formatCurrency(null)).toBe('N/A');
  });

  it('returns N/A for undefined', () => {
    expect(formatCurrency(undefined)).toBe('N/A');
  });
});

// ─── Item 5: Cash Advance status badge tones (DMS-aligned) ───────────────────
describe('Item 5 — Cash Advance status badge mapping', () => {
  it('Approved → success (green)', () => {
    expect(statusTone('Approved')).toBe('success');
  });

  it('Submitted (Pending Review) → info (blue-ish)', () => {
    // "Submitted" maps to info tone; this is what the backend sends for Pending Finance Review
    expect(statusTone('Submitted')).toBe('info');
  });

  it('PendingFinanceManager (Pending Review FM) → warning (amber)', () => {
    expect(statusTone('PendingFinanceManager')).toBe('warning');
  });

  it('Disbursed → info (blue) per DMS mapping', () => {
    // normalizeStatus("Disbursed") → "DISBURSED" → tone: "info"
    expect(normalizeStatus('Disbursed')).toBe('DISBURSED');
    expect(statusTone('Disbursed')).toBe('info');
  });

  it('Completed → success (green)', () => {
    expect(normalizeStatus('Completed')).toBe('COMPLETED');
    expect(statusTone('Completed')).toBe('success');
  });

  it('ReturnedForRevision → danger (red)', () => {
    expect(statusTone('ReturnedForRevision')).toBe('danger');
  });

  it('Draft → neutral (grey)', () => {
    expect(statusTone('Draft')).toBe('neutral');
  });
});
