import React, { useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { useUniqueDocuments } from '../hooks/useDuplicates';
import { DataTable, type Column } from '../components/shared/DataTable';
import FilterBar from '../components/shared/FilterBar';
import StatusBadge from '../components/shared/StatusBadge';
import Button from '../components/Buttons';
import type { UniqueDocumentItem } from '../services/duplicates.service';

const UniqueDocuments: React.FC = () => {
  const { user } = useAuth();
  const {
    items,
    total,
    isLoading,
    error,
    refetch,
    page,
    setPage,
    pageSize,
    search,
    setSearch,
    docTypeFilter,
    setDocTypeFilter,
    dateFrom,
    setDateFrom,
    applyFilters,
    resetFilters,
    scope,
  } = useUniqueDocuments();

  const [selectedDoc, setSelectedDoc] = useState<UniqueDocumentItem | null>(null);

  const columns: Column<UniqueDocumentItem>[] = [
    {
      key: 'id',
      label: 'RECORD ID',
      render: (row) => (
        <span style={{ fontWeight: 700, color: '#0F172A', fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
          {row.id}
        </span>
      ),
    },
    {
      key: 'type',
      label: 'DOCUMENT TYPE',
      render: (row) => {
        const typeStr = row.type || row.documentType || 'Invoice';
        const isInvoice = typeStr.toLowerCase().includes('invoice');
        const isReceipt = typeStr.toLowerCase().includes('receipt');
        const isLiq = typeStr.toLowerCase().includes('liquidation');
        const bg = isInvoice ? '#EFF6FF' : isReceipt ? '#F0FDF4' : '#FEF3C7';
        const color = isInvoice ? '#2563EB' : isReceipt ? '#16A34A' : '#D97706';
        const border = isInvoice ? '#DBEAFE' : isReceipt ? '#DCFCE7' : '#FDE68A';
        return (
          <span
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '5px',
              padding: '3px 10px',
              borderRadius: '6px',
              fontSize: '11px',
              fontWeight: 700,
              background: bg,
              color: color,
              border: `1px solid ${border}`,
              whiteSpace: 'nowrap',
            }}
          >
            <i
              className={isInvoice ? 'ti ti-file-invoice' : isReceipt ? 'ti ti-receipt' : 'ti ti-file-text'}
              style={{ fontSize: '12px' }}
            />
            {typeStr}
          </span>
        );
      },
    },
    {
      key: 'orInvoiceNumber',
      label: 'OR / INVOICE NUMBER',
      render: (row) => (
        <span style={{ fontWeight: 600, color: '#1E293B', fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
          {row.orInvoiceNumber || '—'}
        </span>
      ),
    },
    {
      key: 'clientName',
      label: 'CLIENT / ENTITY',
      render: (row) => (
        <span style={{ fontWeight: 500, color: '#334155' }}>
          {row.clientName}
        </span>
      ),
    },
    {
      key: 'amount',
      label: 'AMOUNT',
      align: 'right',
      render: (row) => (
        <span style={{ fontWeight: 700, color: '#0F172A', fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
          {row.amount !== undefined
            ? `₱${row.amount.toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
            : 'N/A'}
        </span>
      ),
    },
    {
      key: 'transactionDate',
      label: 'TRANSACTION DATE',
      render: (row) => (
        <span style={{ color: '#64748B', fontSize: '12px' }}>
          {row.transactionDate}
        </span>
      ),
    },
    {
      key: 'aiConfidence',
      label: 'UNIQUENESS SCORE',
      render: (row) => (
        <span
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: '4px',
            color: '#0D9488',
            fontWeight: 700,
            background: '#F0FDFA',
            padding: '3px 8px',
            borderRadius: '6px',
            border: '1px solid #CCFBF1',
            fontSize: '12px',
          }}
        >
          <i className="ti ti-shield-check" style={{ fontSize: '13px' }} />
          {row.aiConfidence}% Unique
        </span>
      ),
    },
    {
      key: 'status',
      label: 'STATUS',
      render: (row) => <StatusBadge status={row.status} />,
    },
  ];

  const docTypeOptions =
    scope === 'liquidation'
      ? [{ label: 'Liquidation Voucher', value: 'Liquidation' }]
      : scope === 'own'
      ? [
          { label: 'Invoice', value: 'Invoice' },
          { label: 'Official Receipt', value: 'Receipt' },
        ]
      : [
          { label: 'Invoice', value: 'Invoice' },
          { label: 'Official Receipt', value: 'Receipt' },
          { label: 'Liquidation Voucher', value: 'Liquidation' },
        ];

  const filterFields = [
    {
      key: 'docType',
      label: 'DOCUMENT TYPE',
      type: 'select' as const,
      options: docTypeOptions,
    },
    {
      key: 'dateFrom',
      label: 'TRANSACTION DATE',
      type: 'date' as const,
    },
  ];

  const filterValues = {
    docType: docTypeFilter,
    dateFrom,
  };

  const handleFieldChange = (key: string, value: string) => {
    if (key === 'docType') setDocTypeFilter(value);
    if (key === 'dateFrom') setDateFrom(value);
  };

  const displayedItems = items.filter((row: any) => {
    if (!docTypeFilter || docTypeFilter === 'All') return true;
    const t = (row.type || row.documentType || '').toLowerCase();
    const f = docTypeFilter.toLowerCase();
    if (f === 'invoice') return t.includes('invoice');
    if (f === 'receipt' || f.includes('receipt')) return t.includes('receipt');
    if (f === 'liquidation' || f.includes('liquidation')) return t.includes('liquidation');
    return t.includes(f);
  });

  const effectiveTotal = docTypeFilter && docTypeFilter !== 'All' ? displayedItems.length : (total || displayedItems.length);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {/* Speedex OneUI Header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <h2 style={{ margin: 0, fontSize: '20px', fontWeight: 800, color: '#0F172A', fontFamily: 'var(--fh, Montserrat, sans-serif)' }}>
              Unique Documents
            </h2>
            <span
              style={{
                background: '#F0FDFA',
                color: '#0D9488',
                fontSize: '11px',
                fontWeight: 700,
                padding: '3px 8px',
                borderRadius: '6px',
                border: '1px solid #CCFBF1',
                display: 'inline-flex',
                alignItems: 'center',
                gap: '4px',
              }}
            >
              <i className="ti ti-sparkles" style={{ fontSize: '11px' }} />
              AI Verified
            </span>
          </div>
          <p style={{ margin: '4px 0 0', fontSize: '13px', color: '#64748B' }}>
            Verified non-duplicate financial documents stored in the official repository.
          </p>
        </div>

        <Button
          title="Refresh"
          variant="secondary"
          size="sm"
          onClick={() => refetch()}
        />
      </div>

      {/* Speedex OneUI Filter Bar */}
      <FilterBar
        searchValue={search}
        onSearchChange={setSearch}
        searchPlaceholder="Search by ID, invoice, or client…"
        fields={filterFields}
        values={filterValues}
        onFieldChange={handleFieldChange}
        onApply={applyFilters}
        onReset={resetFilters}
      />

      {/* Speedex OneUI Data Table */}
      <DataTable
        rows={displayedItems}
        columns={columns}
        rowKey={(r) => r.id}
        total={effectiveTotal}
        page={page}
        pageSize={pageSize}
        onPageChange={setPage}
        isLoading={isLoading}
        error={error}
        onRetry={refetch}
        emptyMessage="No unique documents found."
        renderActions={(row) => (
          <Button
            title="Inspect"
            variant="secondary"
            size="sm"
            onClick={() => setSelectedDoc(row)}
          />
        )}
      />

      {/* Speedex OneUI Document Details Modal */}
      {selectedDoc && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(15, 23, 42, 0.55)',
            backdropFilter: 'blur(4px)',
            zIndex: 9999,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            padding: '16px',
            animation: 'fadeIn 0.15s ease',
          }}
          onClick={() => setSelectedDoc(null)}
        >
          <div
            style={{
              background: '#FFFFFF',
              borderRadius: 16,
              padding: '24px 28px',
              maxWidth: 520,
              width: '100%',
              boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
              border: '1px solid #E2E8F0',
            }}
            onClick={(e) => e.stopPropagation()}
          >
            {/* Modal Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <div
                  style={{
                    width: 36,
                    height: 36,
                    borderRadius: 10,
                    background: '#F0FDFA',
                    color: '#0D9488',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    fontSize: '18px',
                  }}
                >
                  <i className="ti ti-file-certificate" />
                </div>
                <div>
                  <h3 style={{ margin: 0, fontSize: '17px', fontWeight: 700, color: '#0F172A' }}>
                    Document Inspection — {selectedDoc.id}
                  </h3>
                  <span style={{ fontSize: '12px', color: '#64748B' }}>
                    Authenticity & uniqueness verification details
                  </span>
                </div>
              </div>
              <button
                onClick={() => setSelectedDoc(null)}
                style={{
                  background: 'none',
                  border: 'none',
                  fontSize: '20px',
                  cursor: 'pointer',
                  color: '#94A3B8',
                  padding: '4px',
                  borderRadius: '6px',
                }}
              >
                &times;
              </button>
            </div>

            {/* Modal Body: Information Rows */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12, fontSize: '13px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid #F1F5F9', paddingBottom: 10 }}>
                <span style={{ color: '#64748B', fontWeight: 500 }}>Document Number</span>
                <span style={{ fontWeight: 700, color: '#0F172A', fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
                  {selectedDoc.orInvoiceNumber || 'N/A'}
                </span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid #F1F5F9', paddingBottom: 10 }}>
                <span style={{ color: '#64748B', fontWeight: 500 }}>Client / Entity</span>
                <span style={{ fontWeight: 600, color: '#1E293B' }}>{selectedDoc.clientName}</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid #F1F5F9', paddingBottom: 10 }}>
                <span style={{ color: '#64748B', fontWeight: 500 }}>Amount</span>
                <span style={{ fontWeight: 700, color: '#0F172A', fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
                  {selectedDoc.amount !== undefined
                    ? `₱${selectedDoc.amount.toLocaleString('en-PH', { minimumFractionDigits: 2 })}`
                    : 'N/A'}
                </span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid #F1F5F9', paddingBottom: 10 }}>
                <span style={{ color: '#64748B', fontWeight: 500 }}>Transaction Date</span>
                <span style={{ color: '#334155' }}>{selectedDoc.transactionDate}</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid #F1F5F9', paddingBottom: 10 }}>
                <span style={{ color: '#64748B', fontWeight: 500 }}>Source System</span>
                <span style={{ fontWeight: 600, color: '#2563EB' }}>{selectedDoc.source}</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid #F1F5F9', paddingBottom: 10 }}>
                <span style={{ color: '#64748B', fontWeight: 500 }}>Uniqueness Confidence</span>
                <span style={{ color: '#0D9488', fontWeight: 700, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                  <i className="ti ti-shield-check" style={{ fontSize: '14px' }} />
                  {selectedDoc.aiConfidence}%
                </span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid #F1F5F9', paddingBottom: 10 }}>
                <span style={{ color: '#64748B', fontWeight: 500 }}>Reviewed By</span>
                <span style={{ color: '#334155' }}>{selectedDoc.reviewedBy}</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingTop: 4 }}>
                <span style={{ color: '#64748B', fontWeight: 500 }}>Validation Status</span>
                <StatusBadge status={selectedDoc.status} />
              </div>
            </div>

            {/* Modal Footer */}
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 24, paddingTop: 16, borderTop: '1px solid #F1F5F9' }}>
              <Button title="Close" variant="secondary" size="sm" onClick={() => setSelectedDoc(null)} />
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default UniqueDocuments;
