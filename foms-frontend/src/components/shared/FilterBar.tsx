import React from 'react';
import Button from '../Buttons';

export interface FilterField {
  key: string;
  label: string;
  type: 'select' | 'date';
  /** For selects: options supplied by the caller (from the API), never hardcoded here. */
  options?: { label: string; value: string }[];
}

export interface FilterBarProps {
  searchValue: string;
  onSearchChange: (value: string) => void;
  searchPlaceholder?: string;
  fields?: FilterField[];
  values: Record<string, string>;
  onFieldChange: (key: string, value: string) => void;
  onApply: () => void;
  onReset: () => void;
}

const inputStyle: React.CSSProperties = {
  height: 38,
  padding: '0 12px',
  border: '1px solid #CBD5E1',
  borderRadius: 8,
  fontSize: '0.875rem',
  background: '#fff',
  color: '#0F172A',
  outline: 'none',
  transition: 'border-color 0.2s, box-shadow 0.2s',
  boxSizing: 'border-box',
};

const labelStyle: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 4,
  fontSize: '0.72rem',
  fontWeight: 700,
  color: '#64748B',
  letterSpacing: '0.04em',
  textTransform: 'uppercase',
};

/** Search + status/date filters with Apply / Reset. Pair with useList. */
export const FilterBar: React.FC<FilterBarProps> = ({
  searchValue,
  onSearchChange,
  searchPlaceholder = 'Search…',
  fields = [],
  values,
  onFieldChange,
  onApply,
  onReset,
}) => (
  <form
    onSubmit={(e) => {
      e.preventDefault();
      onApply();
    }}
    style={{
      background: '#ffffff',
      border: '1px solid #E2E8F0',
      borderRadius: 12,
      padding: '14px 18px',
      boxShadow: '0 1px 3px rgba(15, 23, 42, 0.04)',
      display: 'flex',
      gap: 12,
      flexWrap: 'wrap',
      alignItems: 'flex-end',
      marginBottom: 20,
    }}
  >
    <label style={{ ...labelStyle, flex: '1 1 260px', minWidth: 240 }}>
      Search
      <div style={{ position: 'relative', display: 'flex', alignItems: 'center', width: '100%' }}>
        <i
          className="ti ti-search"
          style={{
            position: 'absolute',
            left: 12,
            color: '#94A3B8',
            fontSize: '16px',
            pointerEvents: 'none',
          }}
        />
        <input
          type="search"
          value={searchValue}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder={searchPlaceholder}
          aria-label="Search"
          style={{
            ...inputStyle,
            width: '100%',
            paddingLeft: 36,
          }}
        />
      </div>
    </label>

    {fields.map((f) => (
      <label key={f.key} style={{ ...labelStyle, flex: '0 1 auto', minWidth: f.type === 'select' ? 160 : 150 }}>
        {f.label}
        {f.type === 'select' ? (
          <select
            value={values[f.key] ?? ''}
            onChange={(e) => onFieldChange(f.key, e.target.value)}
            style={{ ...inputStyle, width: '100%', cursor: 'pointer' }}
          >
            <option value="">All</option>
            {f.options?.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        ) : (
          <input
            type="date"
            value={values[f.key] ?? ''}
            onChange={(e) => onFieldChange(f.key, e.target.value)}
            style={{ ...inputStyle, width: '100%' }}
          />
        )}
      </label>
    ))}

    <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
      <Button
        title="Apply"
        size="md"
        icon="ti-filter"
        onClick={onApply}
      />
      <Button
        title="Reset"
        variant="secondary"
        size="md"
        icon="ti-rotate"
        onClick={onReset}
      />
    </div>
  </form>
);

export default FilterBar;
