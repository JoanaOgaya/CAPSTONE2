import React from 'react';
import { useAuth } from '../../context/AuthContext';
import { LANDING_PATH, DEFAULT_LANDING_ROUTE } from '../../utils/roleAccess';
import { Link } from 'react-router-dom';

/** 
 * Standard 403 Forbidden Page
 * Rendered when authenticated user's role does not have permission for the requested module.
 */
export const ForbiddenPage: React.FC = () => {
  const { user } = useAuth();
  const homePath = user ? (LANDING_PATH[user.role] || DEFAULT_LANDING_ROUTE) : '/login';

  return (
    <div className="forbidden-page-container" style={{ padding: '60px 24px', textAlign: 'center', maxWidth: '520px', margin: '40px auto' }}>
      <div style={{ display: 'inline-flex', padding: '16px', borderRadius: '50%', background: '#fee2e2', color: '#dc2626', marginBottom: '16px' }}>
        <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <circle cx="12" cy="12" r="10" />
          <line x1="4.93" y1="4.93" x2="19.07" y2="19.07" />
        </svg>
      </div>
      <h1 style={{ fontSize: '24px', fontWeight: 700, color: '#1e293b', marginBottom: '12px' }}>
        403 — Access denied
      </h1>
      <p style={{ fontSize: '14px', color: '#64748b', marginBottom: '24px', lineHeight: 1.5 }}>
        Your role does not have permission to view this page.
      </p>
      <Link
        to={homePath}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '8px',
          padding: '10px 20px',
          background: '#0D9488',
          color: '#ffffff',
          borderRadius: '6px',
          textDecoration: 'none',
          fontWeight: 600,
          fontSize: '13px'
        }}
      >
        Return to Dashboard
      </Link>
    </div>
  );
};

export default ForbiddenPage;
