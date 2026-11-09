import React from 'react';
import Button from '../Buttons';
import { EmptyState } from './EmptyState';
import { ErrorState } from './ErrorState';

export interface Column<T> {
  key: string;
  label: string;
  sortable?: boolean;
  align?: 'left' | 'center' | 'right';
  render?: (row: T) => React.ReactNode;
}

export interface DataTableProps<T> {
  rows: T[];
  columns: Column<T>[];
  rowKey: (row: T) => string;
  total: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  sort?: { sortBy: string; sortDir: 'asc' | 'desc' };
  onSort?: (sortBy: string) => void;
  isLoading?: boolean;
  error?: Error | null;
  onRetry?: () => void;
  emptyMessage?: string;
  /** Per-row action column (e.g. <ActionButtons />). */
  renderActions?: (row: T) => React.ReactNode;
}

const th: React.CSSProperties = {
  padding: '12px 18px',
  fontSize: '11px',
  fontWeight: 700,
  color: '#64748B',
  textTransform: 'uppercase',
  letterSpacing: '0.05em',
  borderBottom: '1px solid #E2E8F0',
  background: '#F8FAFC',
};

const td: React.CSSProperties = {
  padding: '14px 18px',
  fontSize: '13px',
  color: '#0F172A',
  borderBottom: '1px solid #F1F5F9',
  verticalAlign: 'middle',
};

/** Server-driven Speedex OneUI DataTable: clean borders, responsive layout, and smooth interactions. */
export function DataTable<T>({
  rows,
  columns,
  rowKey,
  total,
  page,
  pageSize,
  onPageChange,
  sort,
  onSort,
  isLoading,
  error,
  onRetry,
  emptyMessage = 'No records found.',
  renderActions,
}: DataTableProps<T>) {
  if (error) return <ErrorState error={error} onRetry={onRetry} />;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);

  return (
    <div
      style={{
        background: '#FFFFFF',
        border: '1px solid #E2E8F0',
        borderRadius: 12,
        overflow: 'hidden',
        boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.04)',
      }}
    >
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left' }}>
          <thead>
            <tr>
              {columns.map((c) => (
                <th
                  key={c.key}
                  style={{
                    ...th,
                    textAlign: c.align ?? 'left',
                    cursor: c.sortable ? 'pointer' : 'default',
                    userSelect: 'none',
                  }}
                  onClick={c.sortable && onSort ? () => onSort(c.key) : undefined}
                  aria-sort={sort?.sortBy === c.key ? (sort.sortDir === 'asc' ? 'ascending' : 'descending') : undefined}
                >
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                    {c.label}
                    {sort?.sortBy === c.key && (
                      <i className={sort.sortDir === 'asc' ? 'ti ti-arrow-up' : 'ti ti-arrow-down'} style={{ fontSize: '11px', color: '#0D9488' }} />
                    )}
                  </span>
                </th>
              ))}
              {renderActions && (
                <th style={{ ...th, textAlign: 'right' }}>
                  Actions
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={columns.length + (renderActions ? 1 : 0)} style={{ ...td, textAlign: 'center', padding: '36px 18px', color: '#94A3B8' }}>
                  <div style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
                    <i className="ti ti-loader" style={{ animation: 'spin 1s linear infinite' }} />
                    <span>Loading records…</span>
                  </div>
                </td>
              </tr>
            )}
            {!isLoading &&
              rows.map((row) => (
                <tr
                  key={rowKey(row)}
                  style={{ transition: 'background 0.15s ease' }}
                  onMouseEnter={(e) => (e.currentTarget.style.backgroundColor = '#F8FAFC')}
                  onMouseLeave={(e) => (e.currentTarget.style.backgroundColor = 'transparent')}
                >
                  {columns.map((c) => (
                    <td key={c.key} style={{ ...td, textAlign: c.align ?? 'left' }}>
                      {c.render ? c.render(row) : String((row as Record<string, unknown>)[c.key] ?? '')}
                    </td>
                  ))}
                  {renderActions && (
                    <td style={{ ...td, textAlign: 'right', whiteSpace: 'nowrap' }}>
                      {renderActions(row)}
                    </td>
                  )}
                </tr>
              ))}
          </tbody>
        </table>
      </div>

      {!isLoading && rows.length === 0 && <EmptyState message={emptyMessage} />}

      {/* Speedex OneUI Pagination Bar */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          padding: '12px 20px',
          fontSize: '13px',
          color: '#64748B',
          background: '#FAFAFA',
          borderTop: '1px solid #F1F5F9',
          flexWrap: 'wrap',
          gap: 12,
        }}
      >
        <span>
          Showing <strong style={{ color: '#0F172A' }}>{from}–{to}</strong> of <strong style={{ color: '#0F172A' }}>{total}</strong> records
        </span>

        <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
          <Button
            title="Previous"
            variant="secondary"
            size="sm"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
          />
          <span style={{ fontWeight: 600, color: '#334155', minWidth: '90px', textAlign: 'center' }}>
            Page {page} of {totalPages}
          </span>
          <Button
            title="Next"
            variant="secondary"
            size="sm"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
          />
        </div>
      </div>
    </div>
  );
}

export default DataTable;
