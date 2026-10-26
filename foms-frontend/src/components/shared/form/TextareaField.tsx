import React from 'react';

export interface TextareaFieldProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  label: string;
  error?: string;
  success?: string;
  hint?: string;
}

export const TextareaField: React.FC<TextareaFieldProps> = ({
  id,
  label,
  error,
  success,
  hint,
  disabled,
  className = '',
  style,
  rows = 3,
  ...props
}) => {
  const textareaId = id || `textarea-${label.toLowerCase().replace(/[^a-z0-9]/g, '-')}`;
  const isError = Boolean(error);
  const isSuccess = Boolean(success && !isError);

  const getBorderColor = () => {
    if (isError) return 'var(--status-failed)';
    if (isSuccess) return 'var(--status-active)';
    return 'var(--border)';
  };

  return (
    <div className={`form-group ${className}`} style={{ marginBottom: 16, display: 'flex', flexDirection: 'column', gap: 6, ...style }}>
      <label
        htmlFor={textareaId}
        style={{
          fontSize: '0.75rem',
          fontWeight: 700,
          color: isError ? 'var(--status-failed)' : isSuccess ? 'var(--status-active)' : disabled ? 'var(--text-muted)' : 'var(--primary-dark)',
          textTransform: 'uppercase',
          letterSpacing: '0.5px',
        }}
      >
        {label}
      </label>
      <textarea
        id={textareaId}
        disabled={disabled}
        rows={rows}
        style={{
          padding: '10px 12px',
          border: `1.5px solid ${getBorderColor()}`,
          borderRadius: 'var(--radius-sm)',
          background: disabled ? 'var(--bg-body)' : 'var(--bg-card)',
          color: 'var(--text-primary)',
          fontSize: '0.88rem',
          fontWeight: 500,
          outline: 'none',
          transition: 'border-color var(--motion-standard) var(--ease-standard), box-shadow var(--motion-standard) var(--ease-standard)',
          width: '100%',
          cursor: disabled ? 'not-allowed' : 'text',
          opacity: disabled ? 0.6 : 1,
          resize: 'vertical',
        }}
        onFocus={(e) => {
          if (!isError && !isSuccess && !disabled) {
            e.currentTarget.style.borderColor = 'var(--primary)';
            e.currentTarget.style.boxShadow = '0 0 0 3px rgba(0, 169, 157, 0.15)';
          }
        }}
        onBlur={(e) => {
          if (!isError && !isSuccess) {
            e.currentTarget.style.borderColor = 'var(--border)';
            e.currentTarget.style.boxShadow = 'none';
          }
        }}
        {...props}
      />
      {error && (
        <span style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--status-failed)' }}>
          {error.startsWith('⚠') ? error : `⚠ ${error}`}
        </span>
      )}
      {success && !error && (
        <span style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--status-active)' }}>
          {success.startsWith('✓') ? success : `✓ ${success}`}
        </span>
      )}
      {hint && !error && !success && (
        <span style={{ fontSize: '0.8rem', color: 'var(--text-muted)' }}>
          {hint}
        </span>
      )}
    </div>
  );
};

export default TextareaField;
