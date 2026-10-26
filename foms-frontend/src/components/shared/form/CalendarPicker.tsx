import React from 'react';
import { Calendar } from 'lucide-react';
import { TextField } from './TextField';

export interface CalendarPickerProps {
  label: string;
  value: string;
  onChange: (date: string) => void;
  error?: string;
  success?: string;
  hint?: string;
  minDate?: string;
  maxDate?: string;
  disabled?: boolean;
}

export const CalendarPicker: React.FC<CalendarPickerProps> = ({
  label,
  value,
  onChange,
  error,
  success,
  hint,
  minDate,
  maxDate,
  disabled,
}) => {
  return (
    <TextField
      label={label}
      type="date"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      min={minDate}
      max={maxDate}
      disabled={disabled}
      error={error}
      success={success}
      hint={hint}
      prefixIcon={<Calendar size={16} strokeWidth={2} />}
    />
  );
};

export interface CalendarRangePickerProps {
  label?: string;
  startDate: string;
  endDate: string;
  onRangeChange: (start: string, end: string) => void;
  disabled?: boolean;
}

export const CalendarRangePicker: React.FC<CalendarRangePickerProps> = ({
  label = 'Date Period',
  startDate,
  endDate,
  onRangeChange,
  disabled,
}) => {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      {label && (
        <span
          style={{
            fontSize: '0.75rem',
            fontWeight: 700,
            color: 'var(--primary-dark)',
            textTransform: 'uppercase',
            letterSpacing: '0.5px',
          }}
        >
          {label}
        </span>
      )}
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <input
          type="date"
          value={startDate}
          onChange={(e) => onRangeChange(e.target.value, endDate)}
          disabled={disabled}
          style={{
            height: 38,
            padding: '0 10px',
            border: '1.5px solid var(--border)',
            borderRadius: 'var(--radius-sm)',
            background: 'var(--bg-card)',
            color: 'var(--text-primary)',
            fontSize: '0.85rem',
            outline: 'none',
          }}
        />
        <span style={{ color: 'var(--text-muted)', fontSize: '0.85rem' }}>to</span>
        <input
          type="date"
          value={endDate}
          onChange={(e) => onRangeChange(startDate, e.target.value)}
          disabled={disabled}
          style={{
            height: 38,
            padding: '0 10px',
            border: '1.5px solid var(--border)',
            borderRadius: 'var(--radius-sm)',
            background: 'var(--bg-card)',
            color: 'var(--text-primary)',
            fontSize: '0.85rem',
            outline: 'none',
          }}
        />
      </div>
    </div>
  );
};

export default CalendarPicker;
