import React from 'react';

export interface EmptyStateProps {
  message?: string;
  description?: string;
  icon?: string;
  action?: React.ReactNode;
  children?: React.ReactNode;
}

export const EmptyState: React.FC<EmptyStateProps> = ({
  message = 'No records yet',
  description,
  icon = 'ti-shield-check',
  action,
  children,
}) => (
  <div
    style={{
      padding: '44px 20px',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      textAlign: 'center',
      background: '#FAFAFA',
      borderRadius: '0 0 12px 12px',
    }}
  >
    <div
      style={{
        width: 52,
        height: 52,
        borderRadius: '50%',
        background: '#F0FDFA',
        border: '1.5px solid #CCFBF1',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        marginBottom: 12,
        color: '#00A99D',
      }}
    >
      <i className={`ti ${icon}`} style={{ fontSize: '24px' }} />
    </div>
    <p
      style={{
        margin: '0 0 4px',
        fontSize: '0.95rem',
        fontWeight: 700,
        color: '#0F172A',
        fontFamily: 'var(--fh, Montserrat, sans-serif)',
      }}
    >
      {message}
    </p>
    {description && (
      <p
        style={{
          margin: 0,
          fontSize: '0.84rem',
          color: '#64748B',
          maxWidth: 420,
          lineHeight: 1.5,
        }}
      >
        {description}
      </p>
    )}
    {action && <div style={{ marginTop: 14 }}>{action}</div>}
    {children}
  </div>
);

export default EmptyState;
