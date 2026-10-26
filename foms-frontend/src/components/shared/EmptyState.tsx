import React from 'react';
import { LucideIcon, PackageX } from 'lucide-react';

interface EmptyStateProps {
  icon?: LucideIcon;
  title: string;
  description: string;
  actionLabel?: string;
  onAction?: () => void;
  className?: string;
}

export const EmptyState: React.FC<EmptyStateProps> = ({
  icon: Icon = PackageX,
  title,
  description,
  actionLabel,
  onAction,
  className = '',
}) => {
  return (
    <div
      className={`empty-state ${className}`}
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '56px 24px',
        textAlign: 'center',
        gap: '12px',
      }}
      data-testid="empty-state"
    >
      <div
        style={{
          width: 56,
          height: 56,
          borderRadius: 'var(--radius-md)',
          background: 'rgba(0, 169, 157, 0.08)',
          border: '1.5px solid rgba(0, 169, 157, 0.2)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          color: 'var(--primary)',
          marginBottom: 4,
        }}
      >
        <Icon size={32} strokeWidth={1.5} />
      </div>
      <h3
        style={{
          fontSize: '1rem',
          fontWeight: 700,
          color: 'var(--text-primary)',
          margin: 0,
        }}
      >
        {title}
      </h3>
      <p
        style={{
          fontSize: '0.85rem',
          color: 'var(--text-muted)',
          maxWidth: '360px',
          lineHeight: 1.5,
          margin: 0,
        }}
      >
        {description}
      </p>
      {actionLabel && onAction && (
        <button
          type="button"
          onClick={onAction}
          className="btn btn-primary"
          style={{ marginTop: 8 }}
        >
          {actionLabel}
        </button>
      )}
    </div>
  );
};

export default EmptyState;
