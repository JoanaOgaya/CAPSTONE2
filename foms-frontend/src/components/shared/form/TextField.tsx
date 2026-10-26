import React from 'react';

export interface TextFieldProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
  success?: string;
  hint?: string;
  prefixIcon?: React.ReactNode;
}

export const TextField: React.FC<TextFieldProps> = ({
  id,
  label,
  error,
  success,
  hint,
  prefixIcon,
  disabled,
  className = '',
  style,
  ...props
}) => {
  const inputId = id || `input-${label.toLowerCase().replace(/[^a-z0-9]/g, '-')}`;
  const isError = Boolean(error);
  const isSuccess = Boolean(success && !isError);

  const getBorderColor = () => {
    if (isError) return 'var(--status-failed)';
    if (isSuccess) return 'var(--status-active)';
    return 'var(--border)';
  };

  const getBoxShadow = () => {
    if (isError) return '0 0 0 3px rgba(220, 38, 38, 0.1)';
    if (isSuccess) return '0 0 0 3px rgba(5, 150, 105, 0.1)';
    return undefined;
  };

  return (
    <div className={`form-group ${className}`} style={{ marginBottom: 16, display: 'flex', flexDirection: 'column', gap: 6, ...style }}>
      <label
        htmlFor={inputId}
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
      <div style={{ position: 'relative', display: 'flex', alignItems: 'center' }}>
        {prefixIcon && (
          <span
            style={{
              position: 'absolute',
              left: 12,
              color: 'var(--text-secondary)',
              display: 'flex',
              alignItems: 'center',
              pointerEvents: 'none',
            }}
          >
            {prefixIcon}
          </span>
        )}
        <input
          id={inputId}
          disabled={disabled}
          style={{
            height: 38,
            padding: prefixIcon ? '0 12px 0 38px' : '0 12px',
            border: `1.5px solid ${getBorderColor()}`,
            borderRadius: 'var(--radius-sm)',
            background: disabled ? 'var(--bg-body)' : 'var(--bg-card)',
            color: 'var(--text-primary)',
            fontSize: '0.88rem',
            fontWeight: 500,
            outline: 'none',
            transition: 'border-color var(--motion-standard) var(--ease-standard), box-shadow var(--motion-standard) var(--ease-standard)',
            width: '100%',
            boxShadow: getBoxShadow(),
            cursor: disabled ? 'not-allowed' : 'text',
            opacity: disabled ? 0.6 : 1,
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
      </div>
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

export default TextField;
