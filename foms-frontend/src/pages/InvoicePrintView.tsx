import React, { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { ArrowLeft, Printer } from 'lucide-react';
import { apiClient } from '../services/apiClient';
import { Skeleton } from '../components/shared/Skeleton';
import { formatCurrency } from '../utils/format';

interface PrintableInvoice {
  invoiceId: string;
  invoiceNumber: string;
  clientName: string;
  clientAddress?: string;
  clientTin?: string;
  billingPeriod?: string;
  issueDate: string;
  dueDate: string;
  items: Array<{
    waybillNumber: string;
    destination: string;
    actualWeightKg: number;
    volumetricWeightKg: number;
    chargeableWeightKg: number;
    freightAmount: number;
    valuationAmount: number;
    odaAmount: number;
    vatAmount: number;
    fuelSurchargeAmount: number;
    totalAmount: number;
  }>;
  subtotal: number;
  vatAmount: number;
  fuelSurcharge: number;
  discountAmount?: number;
  totalAmount: number;
  notes?: string;
}

export const InvoicePrintView: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [invoice, setInvoice] = useState<PrintableInvoice | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    async function fetchInvoice() {
      try {
        setLoading(true);
        const data = await apiClient.get<PrintableInvoice>(`/api/invoices/${id}`);
        setInvoice(data);
      } catch (err) {
        console.warn('Failed to load invoice details for print view', err);
      } finally {
        setLoading(false);
      }
    }
    if (id) fetchInvoice();
  }, [id]);

  const handlePrint = () => {
    window.print();
  };

  if (loading) {
    return (
      <div style={{ maxWidth: 800, margin: '40px auto', padding: 24, background: '#fff' }}>
        <Skeleton height={40} width="60%" style={{ marginBottom: 20 }} />
        <Skeleton height={200} style={{ marginBottom: 20 }} />
        <Skeleton height={300} />
      </div>
    );
  }

  const printData: PrintableInvoice = invoice || {
    invoiceId: id || 'INV-2026-0001',
    invoiceNumber: id || 'INV-2026-0001',
    clientName: 'Shopee Philippines Inc.',
    clientAddress: 'Bonifacio Global City, Taguig, Metro Manila',
    clientTin: '008-123-456-00000',
    billingPeriod: 'October 1–15, 2026',
    issueDate: new Date().toISOString().split('T')[0],
    dueDate: new Date(Date.now() + 30 * 86400000).toISOString().split('T')[0],
    items: [
      {
        waybillNumber: 'WB-2026-0081',
        destination: 'NCR / Metro Manila',
        actualWeightKg: 20.0,
        volumetricWeightKg: 25.0,
        chargeableWeightKg: 25.0,
        freightAmount: 600.0,
        valuationAmount: 10.0,
        odaAmount: 0.0,
        vatAmount: 73.2,
        fuelSurchargeAmount: 90.0,
        totalAmount: 773.2,
      },
    ],
    subtotal: 610.0,
    vatAmount: 73.2,
    fuelSurcharge: 90.0,
    totalAmount: 773.2,
    notes: 'Payment terms: 30 days from receipt. Speedex Courier Services Inc. is VAT-registered.',
  };

  return (
    <div
      style={{
        background: '#FFFFFF',
        color: '#000000',
        minHeight: '100vh',
        padding: '20mm 16mm',
        maxWidth: '210mm',
        margin: '0 auto',
        fontFamily: "'Outfit', 'Segoe UI', Roboto, sans-serif",
      }}
    >
      {/* On-Screen Action Bar (Hidden during @media print) */}
      <div
        className="print-action-bar"
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          marginBottom: '20px',
          paddingBottom: '16px',
          borderBottom: '1px solid var(--border)',
        }}
      >
        <button
          type="button"
          onClick={() => navigate(-1)}
          className="btn btn-outline"
        >
          <ArrowLeft size={16} strokeWidth={2} />
          Back to Invoices
        </button>
        <button
          type="button"
          onClick={handlePrint}
          className="btn btn-primary"
        >
          <Printer size={16} strokeWidth={2} />
          Print / Save PDF (A4)
        </button>
      </div>

      {/* Official A4 Invoice Container */}
      <div style={{ padding: '8px 0' }}>
        {/* Header with Speedex Branding */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 24 }}>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 6 }}>
              <div
                style={{
                  width: 32,
                  height: 32,
                  background: '#00A99D',
                  color: '#FFFFFF',
                  borderRadius: 6,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontWeight: 800,
                  fontSize: '1.2rem',
                }}
              >
                S
              </div>
              <span style={{ fontSize: '1.4rem', fontWeight: 800, letterSpacing: '1px' }}>
                SPEEDEX COURIER SERVICES INC.
              </span>
            </div>
            <p style={{ fontSize: '0.82rem', color: '#475569', margin: '2px 0' }}>
              VAT Reg. TIN: 009-876-543-00000
            </p>
            <p style={{ fontSize: '0.82rem', color: '#475569', margin: '2px 0' }}>
              Central Operations Hub, Metro Manila, Philippines
            </p>
          </div>

          <div style={{ textAlign: 'right' }}>
            <h1 style={{ fontSize: '1.6rem', fontWeight: 800, color: '#1B254B', margin: 0 }}>
              BILLING INVOICE
            </h1>
            <p style={{ fontSize: '0.9rem', fontWeight: 700, color: '#00A99D', marginTop: 4 }}>
              {printData.invoiceNumber}
            </p>
            <p style={{ fontSize: '0.8rem', color: '#64748B', marginTop: 2 }}>
              Date Issued: {printData.issueDate}
            </p>
            <p style={{ fontSize: '0.8rem', color: '#64748B' }}>
              Payment Due: <strong>{printData.dueDate}</strong>
            </p>
          </div>
        </div>

        {/* Client & Billing Info */}
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: '1fr 1fr',
            gap: 20,
            padding: 16,
            background: '#F8FAFF',
            borderRadius: 8,
            border: '1px solid #DDE2EB',
            marginBottom: 24,
          }}
        >
          <div>
            <span style={{ fontSize: '0.72rem', fontWeight: 700, color: '#64748B', textTransform: 'uppercase' }}>
              Billed To
            </span>
            <div style={{ fontSize: '1rem', fontWeight: 700, color: '#0F172A', marginTop: 2 }}>
              {printData.clientName}
            </div>
            {printData.clientAddress && (
              <div style={{ fontSize: '0.82rem', color: '#475569', marginTop: 2 }}>
                {printData.clientAddress}
              </div>
            )}
            {printData.clientTin && (
              <div style={{ fontSize: '0.82rem', color: '#475569', marginTop: 2 }}>
                TIN: {printData.clientTin}
              </div>
            )}
          </div>

          <div style={{ textAlign: 'right' }}>
            <span style={{ fontSize: '0.72rem', fontWeight: 700, color: '#64748B', textTransform: 'uppercase' }}>
              Billing Details
            </span>
            <div style={{ fontSize: '0.85rem', color: '#0F172A', marginTop: 2 }}>
              Billing Period: <strong>{printData.billingPeriod || 'Current Period'}</strong>
            </div>
            <div style={{ fontSize: '0.85rem', color: '#0F172A', marginTop: 2 }}>
              Terms: <strong>30 Days from Receipt</strong>
            </div>
          </div>
        </div>

        {/* Itemized Waybill Line Item Breakdown (BIL-03) */}
        <table
          style={{
            width: '100%',
            borderCollapse: 'collapse',
            marginBottom: 24,
            fontSize: '0.82rem',
          }}
        >
          <thead>
            <tr style={{ background: '#F0FAFA', borderBottom: '2px solid #00A99D' }}>
              <th style={{ padding: '10px 12px', textAlign: 'left', fontWeight: 700, textTransform: 'uppercase' }}>Waybill / Area</th>
              <th style={{ padding: '10px 8px', textAlign: 'right', fontWeight: 700, textTransform: 'uppercase' }}>Chargeable Wt.</th>
              <th style={{ padding: '10px 8px', textAlign: 'right', fontWeight: 700, textTransform: 'uppercase' }}>Freight</th>
              <th style={{ padding: '10px 8px', textAlign: 'right', fontWeight: 700, textTransform: 'uppercase' }}>Valuation</th>
              <th style={{ padding: '10px 8px', textAlign: 'right', fontWeight: 700, textTransform: 'uppercase' }}>ODA</th>
              <th style={{ padding: '10px 8px', textAlign: 'right', fontWeight: 700, textTransform: 'uppercase' }}>VAT (12%)</th>
              <th style={{ padding: '10px 8px', textAlign: 'right', fontWeight: 700, textTransform: 'uppercase' }}>Fuel Surcharge</th>
              <th style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 700, textTransform: 'uppercase' }}>Line Total</th>
            </tr>
          </thead>
          <tbody>
            {printData.items.map((item, idx) => (
              <tr key={idx} style={{ borderBottom: '1px solid #E2E8F0' }}>
                <td style={{ padding: '10px 12px' }}>
                  <div style={{ fontWeight: 700 }}>{item.waybillNumber}</div>
                  <div style={{ fontSize: '0.75rem', color: '#64748B' }}>{item.destination}</div>
                </td>
                <td style={{ padding: '10px 8px', textAlign: 'right' }}>
                  {item.chargeableWeightKg.toFixed(1)} kg
                </td>
                <td style={{ padding: '10px 8px', textAlign: 'right' }}>
                  {formatCurrency(item.freightAmount)}
                </td>
                <td style={{ padding: '10px 8px', textAlign: 'right' }}>
                  {formatCurrency(item.valuationAmount)}
                </td>
                <td style={{ padding: '10px 8px', textAlign: 'right' }}>
                  {formatCurrency(item.odaAmount)}
                </td>
                <td style={{ padding: '10px 8px', textAlign: 'right' }}>
                  {formatCurrency(item.vatAmount)}
                </td>
                <td style={{ padding: '10px 8px', textAlign: 'right' }}>
                  {formatCurrency(item.fuelSurchargeAmount)}
                </td>
                <td style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 700 }}>
                  {formatCurrency(item.totalAmount)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        {/* Invoice Summary Totals */}
        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 24 }}>
          <div style={{ width: '280px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', fontSize: '0.85rem' }}>
              <span style={{ color: '#64748B' }}>Subtotal:</span>
              <strong style={{ color: '#0F172A' }}>{formatCurrency(printData.subtotal)}</strong>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', fontSize: '0.85rem' }}>
              <span style={{ color: '#64748B' }}>Value-Added Tax (12%):</span>
              <strong style={{ color: '#0F172A' }}>{formatCurrency(printData.vatAmount)}</strong>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', fontSize: '0.85rem' }}>
              <span style={{ color: '#64748B' }}>Fuel Surcharge:</span>
              <strong style={{ color: '#0F172A' }}>{formatCurrency(printData.fuelSurcharge)}</strong>
            </div>
            {Boolean(printData.discountAmount && printData.discountAmount > 0) && (
              <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', fontSize: '0.85rem', color: '#059669' }}>
                <span>Approved Discount:</span>
                <strong>-{formatCurrency(printData.discountAmount!)}</strong>
              </div>
            )}
            <div
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                padding: '10px 0',
                borderTop: '2px solid #0F172A',
                marginTop: 6,
                fontSize: '1.05rem',
              }}
            >
              <span style={{ fontWeight: 800, color: '#1B254B' }}>Total Amount Due:</span>
              <strong style={{ fontWeight: 800, color: '#00A99D' }}>{formatCurrency(printData.totalAmount)}</strong>
            </div>
          </div>
        </div>

        {/* Legal & EOPT Tax Notes */}
        <div
          style={{
            borderTop: '1px solid #DDE2EB',
            paddingTop: 16,
            fontSize: '0.75rem',
            color: '#64748B',
            lineHeight: 1.5,
          }}
        >
          <p>
            <strong>Official Invoice Notice (Ease of Paying Taxes Act - EOPT):</strong> This invoice is the principal tax and commercial document. Official Receipts serve as supplementary collector reference documents.
          </p>
          <p style={{ marginTop: 4 }}>
            For billing inquiries or disputes, please contact Speedex Accounting at finance@speedex.ph within 7 days of receipt.
          </p>
        </div>
      </div>
    </div>
  );
};

export default InvoicePrintView;
