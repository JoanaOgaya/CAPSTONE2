import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import { normalizeClientKey, doesWaybillBelongToClient, InvoiceCreation } from '../pages/InvoiceCreation';
import InvoicingDesk from '../pages/InvoicingDesk';
import * as AuthContextModule from '../context/AuthContext';
import { ToastProvider } from '../components/ToastContext';
import type { Client, Waybill, Invoice } from '../types/entities';

// Mock services for InvoicingDesk and InvoiceCreation
vi.mock('../services/invoicesService', () => ({
  invoicesService: {
    list: vi.fn().mockResolvedValue({
      items: [
        {
          id: 'inv-test-1',
          invoiceNumber: 'INV-2026-001',
          clientName: 'Lazada Philippines',
          clientId: 'LZD-001',
          totalAmount: 15000,
          amountPaid: 0,
          balance: 15000,
          dueDate: '2026-12-01',
          status: 'Draft',
          paymentStatus: 'Unpaid',
          issueDate: '2026-11-01',
          currency: 'PHP',
          createdAt: '2026-11-01',
          updatedAt: '2026-11-01',
        } as unknown as Invoice
      ],
      total: 1,
    }),
    create: vi.fn().mockResolvedValue({ id: 'inv-new', invoiceNumber: 'INV-NEW-001' }),
    update: vi.fn().mockResolvedValue({ id: 'inv-test-1' }),
  },
}));

vi.mock('../services/clientsService', () => ({
  clientsService: {
    list: vi.fn().mockResolvedValue({
      items: [
        { id: 'LZD-001', clientCode: 'LZD-001', name: 'Lazada Philippines', billingSchedule: 'Monthly' } as Client,
        { id: 'SHP-001', clientCode: 'SHP-001', name: 'Shopee Logistics', billingSchedule: 'Semi-Monthly' } as Client,
      ],
      total: 2,
    }),
  },
}));

vi.mock('../services/paymentsService', () => ({
  paymentsService: {
    list: vi.fn().mockResolvedValue({ items: [], total: 0 }),
  },
}));

vi.mock('../services/ratesService', () => ({
  ratesService: {
    list: vi.fn().mockResolvedValue({ items: [], total: 0 }),
  },
}));

vi.mock('../services/waybillsService', () => ({
  waybillsService: {
    list: vi.fn().mockResolvedValue({
      items: [
        {
          id: 'wb-test-1',
          waybillNumber: 'WB-TEST-001',
          clientCode: 'LZD-001',
          clientId: 'LZD-001',
          clientName: 'Lazada Philippines',
          status: 'Validated',
          isBilled: false,
          deliveryDate: '2026-11-10',
          area: 'NCR',
          senderName: 'Lazada Warehouse',
          receiverName: 'Juan Dela Cruz',
          receiverAddress: 'Makati City',
          itemQuantity: 1,
          declaredValue: 2500,
          encodedBy: 'DMS Dispatch System',
          podUrl: 'https://speedex.storage.blob/pods/WB-TEST-001-pod.jpg',
        } as unknown as Waybill,
        {
          id: 'wb-test-2',
          waybillNumber: 'WB-TEST-002',
          clientCode: 'SHP-001',
          clientId: 'SHP-001',
          clientName: 'Shopee Logistics',
          status: 'Validated',
          isBilled: false,
          deliveryDate: '2026-11-11',
          area: 'NCR',
          senderName: 'Shopee Hub',
          receiverName: 'Maria Santos',
          receiverAddress: 'Quezon City',
          itemQuantity: 2,
          declaredValue: 1200,
          encodedBy: 'Driver App',
          // No podUrl present
        } as unknown as Waybill,
      ],
      total: 2,
    }),
  },
}));

function mockAuth(role: string = 'ACCOUNTANT') {
  vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
    user: {
      employeeId: 'EMP-003',
      fullName: 'John Accountant',
      role: role as any,
      email: 'john.accountant@speedex.ph',
    },
    token: 'mock-token',
    login: vi.fn(),
    logout: vi.fn(),
    isAuthenticated: true,
    isLoading: false,
  });
}

describe('Salvaged notsodone Features', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('1. normalizeClientKey & doesWaybillBelongToClient', () => {
    it('normalizes Lazada aliases and keys to LZD-001', () => {
      expect(normalizeClientKey('1')).toBe('LZD-001');
      expect(normalizeClientKey('ca-001')).toBe('LZD-001');
      expect(normalizeClientKey('lzd-001')).toBe('LZD-001');
      expect(normalizeClientKey('Lazada Express PH')).toBe('LZD-001');
    });

    it('normalizes Shopee aliases and keys to SHP-001', () => {
      expect(normalizeClientKey('2')).toBe('SHP-001');
      expect(normalizeClientKey('ca-002')).toBe('SHP-001');
      expect(normalizeClientKey('shp-001')).toBe('SHP-001');
      expect(normalizeClientKey('Shopee Xpress')).toBe('SHP-001');
    });

    it('normalizes TikTok aliases and keys to TKT-001', () => {
      expect(normalizeClientKey('3')).toBe('TKT-001');
      expect(normalizeClientKey('ca-003')).toBe('TKT-001');
      expect(normalizeClientKey('tkt-001')).toBe('TKT-001');
      expect(normalizeClientKey('TikTok Shop')).toBe('TKT-001');
    });

    it('handles unknown clients and null/undefined values gracefully', () => {
      expect(normalizeClientKey('acme-logistics')).toBe('acme-logistics');
      expect(normalizeClientKey('')).toBe('');
      expect(normalizeClientKey(null)).toBe('');
      expect(normalizeClientKey(undefined)).toBe('');
    });

    it('matches waybill to client using normalizeClientKey and metadata', () => {
      const lazadaClient: Client = { id: 'LZD-001', clientCode: 'LZD-001', name: 'Lazada Philippines' } as Client;
      const shopeeClient: Client = { id: 'SHP-001', clientCode: 'SHP-001', name: 'Shopee Logistics' } as Client;

      const lazadaWaybill = { clientCode: 'ca-001', clientName: 'Lazada' };
      const shopeeWaybill = { clientId: '2', clientName: 'Shopee' };

      expect(doesWaybillBelongToClient(lazadaWaybill, lazadaClient)).toBe(true);
      expect(doesWaybillBelongToClient(lazadaWaybill, shopeeClient)).toBe(false);

      expect(doesWaybillBelongToClient(shopeeWaybill, shopeeClient)).toBe(true);
      expect(doesWaybillBelongToClient(shopeeWaybill, lazadaClient)).toBe(false);
    });
  });

  describe('2. InvoicingDesk Modal Integration', () => {
    it('opens InvoiceCreation modal when "Create New Invoice" is clicked and closes on close button', async () => {
      mockAuth('ACCOUNTANT');

      render(
        <MemoryRouter>
          <ToastProvider>
            <InvoicingDesk />
          </ToastProvider>
        </MemoryRouter>
      );

      // Verify page loaded
      expect(await screen.findByText('Client sales invoices, status tracking, and dispatch records')).toBeDefined();

      // Find and click "Create New Invoice" button
      const createBtn = screen.getByTestId('invoicing-create-invoice-btn');
      expect(createBtn).toBeDefined();
      fireEvent.click(createBtn);

      // Verify modal is open
      await waitFor(() => {
        expect(screen.getByTestId('invoicing-create-modal')).toBeDefined();
        expect(screen.getByRole('heading', { level: 2, name: 'Create New Invoice' })).toBeDefined();
      });

      // Find close button and close modal
      const closeBtn = screen.getByTestId('close-create-modal-btn');
      fireEvent.click(closeBtn);

      // Verify modal closes
      await waitFor(() => {
        expect(screen.queryByTestId('invoicing-create-modal')).toBeNull();
      });
    });
  });

  describe('3. Proof of Delivery (POD) Viewer in Waybill Details', () => {
    it('renders recipient, verification badge, and POD link only when image exists', async () => {
      mockAuth('ACCOUNTANT');

      render(
        <MemoryRouter>
          <ToastProvider>
            <InvoiceCreation isModal={true} />
          </ToastProvider>
        </MemoryRouter>
      );

      // Step 1: Select client
      await waitFor(() => {
        expect(screen.getByText('Lazada Philippines')).toBeDefined();
      });
      fireEvent.click(screen.getByText('Lazada Philippines'));

      // In Step 2: Waybill list is displayed. Wait for waybill
      await waitFor(() => {
        expect(screen.getByText('WB-TEST-001')).toBeDefined();
      });

      // Click "View Details" to open slide-over modal
      const detailBtn = await screen.findByRole('button', { name: /View Details/i });
      fireEvent.click(detailBtn);

      await waitFor(() => {
        expect(screen.getByTestId('pod-viewer-section')).toBeDefined();
        expect(screen.getByText('PROOF OF DELIVERY (POD)')).toBeDefined();
        expect(screen.getByText('VERIFIED')).toBeDefined();
        expect(screen.getByText('Original Signed & Matched ✓')).toBeDefined();
        expect(screen.getByTestId('pod-image-link')).toBeDefined();
      });
    });
  });
});
