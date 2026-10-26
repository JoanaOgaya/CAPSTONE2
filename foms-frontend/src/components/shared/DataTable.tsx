import React, { useState, useMemo } from 'react';
import { Search, Plus, LucideIcon } from 'lucide-react';
import { SkeletonTable } from './Skeleton';
import { EmptyState } from './EmptyState';

export interface ColumnDef<T> {
  key: string;
  header?: string;
  label?: string; // Backward compatibility alias for header
  render?: (row: T) => React.ReactNode;
  sortable?: boolean;
  width?: string;
  align?: 'left' | 'center' | 'right';
}

// Backward-compatible alias for older pages
export type Column<T = any> = ColumnDef<T>;

export interface DataTableProps<T = Record<string, any>> {
  columns: ColumnDef<T>[];
  data?: T[];
  // Backward compatibility alias for data
  rows?: T[];
  keyField?: keyof T;
  rowKey?: (row: T) => string | number;
  isLoading?: boolean;
  loading?: boolean;
  error?: any;
  onRetry?: () => void;
  searchPlaceholder?: string;
  searchFilter?: (row: T, term: string) => boolean;
  primaryActionLabel?: string;
  primaryActionIcon?: LucideIcon;
  onPrimaryAction?: () => void;
  emptyTitle?: string;
  emptyDescription?: string;
  emptyMessage?: string;
  emptyActionLabel?: string;
  onEmptyAction?: () => void;
  filterSlot?: React.ReactNode;
  isRowInactive?: (row: T) => boolean;
  onRowClick?: (row: T) => void;
  renderActions?: (row: T) => React.ReactNode;
  title?: string;
  total?: number;
  page?: number;
  pageSize?: number;
  onPageChange?: (page: number) => void;
}

export function DataTable<T extends Record<string, any>>({
  columns,
  data,
  rows,
  keyField,
  rowKey,
  isLoading,
  loading,
  searchPlaceholder = 'Search records...',
  searchFilter,
  primaryActionLabel,
  primaryActionIcon: PrimaryIcon = Plus,
  onPrimaryAction,
  emptyTitle = 'No records available.',
  emptyDescription,
  emptyMessage,
  emptyActionLabel,
  onEmptyAction,
  filterSlot,
  isRowInactive,
  onRowClick,
  renderActions,
}: DataTableProps<T>) {
  const [searchTerm, setSearchTerm] = useState('');
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');

  const resolvedData = useMemo(() => rows || data || [], [rows, data]);
  const resolvedLoading = isLoading !== undefined ? isLoading : Boolean(loading);
  const resolvedEmptyDesc = emptyDescription || emptyMessage || 'There are currently no items recorded in this view.';

  // Filter & Sort
  const processedData = useMemo(() => {
    let result = [...resolvedData];

    // Search filter
    if (searchTerm.trim() && searchFilter) {
      result = result.filter((row) => searchFilter(row, searchTerm.trim()));
    } else if (searchTerm.trim()) {
      const term = searchTerm.toLowerCase();
      result = result.filter((row) =>
        Object.values(row).some((val) => String(val ?? '').toLowerCase().includes(term))
      );
    }

    // Sort active vs inactive: Inactive items always pushed to bottom
    if (isRowInactive) {
      result.sort((a, b) => {
        const aInactive = isRowInactive(a);
        const bInactive = isRowInactive(b);
        if (aInactive && !bInactive) return 1;
        if (!aInactive && bInactive) return -1;
        return 0;
      });
    }

    // Sort by column
    if (sortKey) {
      result.sort((a, b) => {
        const aVal = a[sortKey];
        const bVal = b[sortKey];
        if (aVal === bVal) return 0;
        if (aVal === null || aVal === undefined) return 1;
        if (bVal === null || bVal === undefined) return -1;
        const cmp = aVal < bVal ? -1 : 1;
        return sortDir === 'asc' ? cmp : -cmp;
      });
    }

    return result;
  }, [resolvedData, searchTerm, searchFilter, sortKey, sortDir, isRowInactive]);

  const handleHeaderClick = (col: ColumnDef<T>) => {
    if (!col.sortable) return;
    if (sortKey === col.key) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortKey(col.key);
      setSortDir('asc');
    }
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {/* Table Toolbar */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 12,
        }}
      >
        {/* Left: Search */}
        <div style={{ position: 'relative', minWidth: 260, flex: '1 1 260px', maxWidth: 360 }}>
          <Search
            size={16}
            style={{
              position: 'absolute',
              left: 12,
              top: '50%',
              transform: 'translateY(-50%)',
              color: 'var(--text-muted)',
              pointerEvents: 'none',
            }}
          />
          <input
            type="text"
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            placeholder={searchPlaceholder}
            style={{
              width: '100%',
              height: 38,
              padding: '0 12px 0 36px',
              border: '1.5px solid var(--border)',
              borderRadius: 'var(--radius-sm)',
              background: 'var(--bg-card)',
              color: 'var(--text-primary)',
              fontSize: '0.85rem',
              outline: 'none',
              transition: 'border-color var(--motion-standard) var(--ease-standard)',
            }}
          />
        </div>

        {/* Center: Filters */}
        {filterSlot && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            {filterSlot}
          </div>
        )}

        {/* Right: Primary Action with visible text label */}
        {primaryActionLabel && onPrimaryAction && (
          <button
            type="button"
            onClick={onPrimaryAction}
            className="btn btn-primary"
            style={{ marginLeft: 'auto' }}
          >
            <PrimaryIcon size={16} strokeWidth={2} />
            {primaryActionLabel}
          </button>
        )}
      </div>

      {/* Table Container */}
      <div
        className="data-table-wrapper"
        style={{
          width: '100%',
          overflowX: 'auto',
          borderRadius: 'var(--radius-md)',
          border: '1px solid var(--border)',
          background: 'var(--bg-card)',
          boxShadow: 'var(--shadow-card)',
        }}
      >
        {resolvedLoading ? (
          <SkeletonTable rows={5} columns={columns.length + (renderActions ? 1 : 0)} />
        ) : processedData.length === 0 ? (
          <EmptyState
            title={emptyTitle}
            description={resolvedEmptyDesc}
            actionLabel={emptyActionLabel || primaryActionLabel}
            onAction={onEmptyAction || onPrimaryAction}
          />
        ) : (
          <table
            className="data-table"
            style={{
              width: '100%',
              borderCollapse: 'separate',
              borderSpacing: 0,
              textAlign: 'left',
            }}
          >
            <thead>
              <tr>
                {columns.map((col) => (
                  <th
                    key={col.key}
                    onClick={() => handleHeaderClick(col)}
                    style={{
                      padding: '16px 18px',
                      fontSize: '0.75rem',
                      fontWeight: 700,
                      color: 'var(--text-primary)',
                      letterSpacing: '0.08em',
                      textTransform: 'uppercase',
                      borderBottom: '2px solid var(--border)',
                      background: 'rgba(0, 169, 157, 0.08)',
                      cursor: col.sortable ? 'pointer' : 'default',
                      userSelect: 'none',
                      width: col.width,
                    }}
                  >
                    <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                      {col.header || col.label}
                      {col.sortable && sortKey === col.key && (
                        <span>{sortDir === 'asc' ? '↑' : '↓'}</span>
                      )}
                    </div>
                  </th>
                ))}
                {renderActions && (
                  <th
                    style={{
                      padding: '16px 18px',
                      fontSize: '0.75rem',
                      fontWeight: 700,
                      color: 'var(--text-primary)',
                      letterSpacing: '0.08em',
                      textTransform: 'uppercase',
                      borderBottom: '2px solid var(--border)',
                      background: 'rgba(0, 169, 157, 0.08)',
                      textAlign: 'right',
                    }}
                  >
                    Actions
                  </th>
                )}
              </tr>
            </thead>
            <tbody>
              {processedData.map((row, idx) => {
                const inactive = isRowInactive ? isRowInactive(row) : false;
                const rowKeyValue = rowKey ? rowKey(row) : keyField ? row[keyField] : idx;
                return (
                  <tr
                    key={String(rowKeyValue)}
                    onClick={() => onRowClick && onRowClick(row)}
                    style={{
                      cursor: onRowClick ? 'pointer' : 'default',
                      opacity: inactive ? 0.45 : 1,
                      backgroundColor: inactive ? 'var(--bg-body)' : 'transparent',
                      transition: 'background-color 0.25s ease',
                    }}
                    onMouseEnter={(e) => {
                      if (!inactive) {
                        e.currentTarget.style.backgroundColor = 'rgba(0, 169, 157, 0.04)';
                      }
                    }}
                    onMouseLeave={(e) => {
                      if (!inactive) {
                        e.currentTarget.style.backgroundColor = 'transparent';
                      }
                    }}
                  >
                    {columns.map((col) => (
                      <td
                        key={col.key}
                        style={{
                          padding: '16px 18px',
                          fontSize: '0.88rem',
                          color: 'var(--text-secondary)',
                          borderBottom: '1px solid var(--border)',
                          verticalAlign: 'middle',
                        }}
                      >
                        {col.render ? col.render(row) : (row[col.key] ?? 'Not yet provided')}
                      </td>
                    ))}
                    {renderActions && (
                      <td
                        style={{
                          padding: '16px 18px',
                          borderBottom: '1px solid var(--border)',
                          textAlign: 'right',
                          verticalAlign: 'middle',
                        }}
                      >
                        {renderActions(row)}
                      </td>
                    )}
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}

export default DataTable;
