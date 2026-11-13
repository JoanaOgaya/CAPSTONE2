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
  fontSize: '13px',
  background: '#FFFFFF',
  color: '#0F172A',
  outline: 'none',
  fontFamily: 'inherit',
  transition: 'border-color 0.15s ease, box-shadow 0.15s ease',
};

/** Speedex OneUI FilterBar with search icon, responsive chips, and polished controls. */
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
      display: 'flex',
      gap: 12,
      flexWrap: 'wrap',
      alignItems: 'flex-end',
      background: '#FFFFFF',
      padding: '16px 20px',
      borderRadius: 12,
      border: '1px solid #E2E8F0',
      boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.04)',
      marginBottom: 16,
    }}
  >
    {/* OneUI Search with leading icon */}
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4, flex: '1 1 240px', minWidth: 220 }}>
      <span style={{ fontSize: '11px', fontWeight: 700, color: '#64748B', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
        Search
      </span>
      <div style={{ position: 'relative', width: '100%' }}>
        <i
          className="ti ti-search"
          style={{
            position: 'absolute',
            left: 12,
            top: '50%',
            transform: 'translateY(-50%)',
            color: '#94A3B8',
            fontSize: '15px',
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
          onFocus={(e) => {
            e.currentTarget.style.borderColor = '#0D9488';
            e.currentTarget.style.boxShadow = '0 0 0 3px rgba(13, 148, 136, 0.12)';
          }}
          onBlur={(e) => {
            e.currentTarget.style.borderColor = '#CBD5E1';
            e.currentTarget.style.boxShadow = 'none';
          }}
        />
      </div>
    </div>

    {/* Dynamic Select and Date Filter Fields */}
    {fields.map((f) => (
      <label
        key={f.key}
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: 4,
          fontSize: '11px',
          fontWeight: 700,
          color: '#64748B',
          textTransform: 'uppercase',
          letterSpacing: '0.04em',
        }}
      >
        {f.label}
        {f.type === 'select' ? (
          <select
            value={values[f.key] ?? ''}
            onChange={(e) => onFieldChange(f.key, e.target.value)}
            style={{
              ...inputStyle,
              minWidth: 160,
              cursor: 'pointer',
            }}
            onFocus={(e) => {
              e.currentTarget.style.borderColor = '#0D9488';
              e.currentTarget.style.boxShadow = '0 0 0 3px rgba(13, 148, 136, 0.12)';
            }}
            onBlur={(e) => {
              e.currentTarget.style.borderColor = '#CBD5E1';
              e.currentTarget.style.boxShadow = 'none';
            }}
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
            style={{
              ...inputStyle,
              minWidth: 150,
              cursor: 'pointer',
            }}
            onFocus={(e) => {
              e.currentTarget.style.borderColor = '#0D9488';
              e.currentTarget.style.boxShadow = '0 0 0 3px rgba(13, 148, 136, 0.12)';
            }}
            onBlur={(e) => {
              e.currentTarget.style.borderColor = '#CBD5E1';
              e.currentTarget.style.boxShadow = 'none';
            }}
          />
        )}
      </label>
    ))}

    {/* Action Buttons */}
    <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
      <Button title="Apply" variant="primary" size="sm" onClick={onApply} />
      <Button title="Reset" variant="secondary" size="sm" onClick={onReset} />
    </div>
  </form>
);

export default FilterBar;
