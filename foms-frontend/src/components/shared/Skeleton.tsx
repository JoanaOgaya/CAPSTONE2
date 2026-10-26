import React from 'react';

interface SkeletonProps {
  className?: string;
  width?: string | number;
  height?: string | number;
  borderRadius?: string;
  style?: React.CSSProperties;
}

export const Skeleton: React.FC<SkeletonProps> = ({
  className = '',
  width,
  height,
  borderRadius = 'var(--radius-sm)',
  style = {},
}) => {
  return (
    <div
      className={`skeleton ${className}`}
      style={{
        width: width !== undefined ? width : '100%',
        height: height !== undefined ? height : '16px',
        borderRadius,
        ...style,
      }}
      aria-hidden="true"
    />
  );
};

export const SkeletonText: React.FC<{ lines?: number; className?: string }> = ({
  lines = 3,
  className = '',
}) => (
  <div className={`skeleton-text-group ${className}`} style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
    {Array.from({ length: lines }).map((_, i) => (
      <Skeleton
        key={i}
        height={14}
        width={i === lines - 1 ? '60%' : i % 2 === 0 ? '90%' : '100%'}
      />
    ))}
  </div>
);

export const SkeletonTitle: React.FC<{ width?: string | number }> = ({ width = '40%' }) => (
  <Skeleton height={24} width={width} borderRadius="var(--radius-sm)" style={{ marginBottom: 12 }} />
);

export const SkeletonButton: React.FC<{ width?: string | number; height?: string | number }> = ({
  width = 110,
  height = 38,
}) => <Skeleton width={width} height={height} borderRadius="var(--radius-sm)" />;

export const SkeletonBadge: React.FC = () => (
  <Skeleton width={70} height={20} borderRadius="var(--radius-full)" />
);

export const SkeletonAvatar: React.FC<{ size?: number }> = ({ size = 36 }) => (
  <Skeleton width={size} height={size} borderRadius="var(--radius-sm)" />
);

export const SkeletonTable: React.FC<{ rows?: number; columns?: number }> = ({
  rows = 5,
  columns = 5,
}) => (
  <div
    style={{
      border: '1px solid var(--border)',
      borderRadius: 'var(--radius-md)',
      background: 'var(--bg-card)',
      overflow: 'hidden',
    }}
  >
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: `repeat(${columns}, 1fr)`,
        gap: 16,
        padding: '16px 18px',
        background: 'rgba(0, 169, 157, 0.08)',
        borderBottom: '2px solid var(--border)',
      }}
    >
      {Array.from({ length: columns }).map((_, i) => (
        <Skeleton key={i} height={14} width="70%" />
      ))}
    </div>
    {Array.from({ length: rows }).map((_, r) => (
      <div
        key={r}
        style={{
          display: 'grid',
          gridTemplateColumns: `repeat(${columns}, 1fr)`,
          gap: 16,
          padding: '16px 18px',
          borderBottom: r === rows - 1 ? 'none' : '1px solid var(--border)',
        }}
      >
        {Array.from({ length: columns }).map((_, c) => (
          <Skeleton key={c} height={14} width={c === 0 ? '80%' : c === 2 ? '50%' : '65%'} />
        ))}
      </div>
    ))}
  </div>
);

export default Skeleton;
