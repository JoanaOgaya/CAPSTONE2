/**
 * Route guards.
 *  - ProtectedRoute: unauthenticated -> /login.
 *  - RoleGuard: authenticated but role not allowed for the module -> 403 page.
 * Access rules come exclusively from utils/roleAccess.
 */
import React from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { DEFAULT_LANDING_ROUTE, getModule, LANDING_PATH, type ModuleKey } from '../utils/roleAccess';

export function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { user, isLoading } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', height: '100vh', width: '100vw', background: '#0f172a', color: '#f8fafc', gap: '16px' }}>
        <div style={{ width: '42px', height: '42px', border: '3px solid rgba(255,255,255,0.15)', borderTopColor: '#38bdf8', borderRadius: '50%', animation: 'spin 0.9s linear infinite' }} />
        <div style={{ fontSize: '14px', fontWeight: 600, letterSpacing: '0.5px' }}>Loading Speedex Logistics Portal...</div>
      </div>
    );
  }
  if (!user) {
    const isLoggedOut = typeof window !== 'undefined' && sessionStorage.getItem('loggedOut') === 'true';
    return <Navigate to="/login" replace state={isLoggedOut ? null : { from: location.pathname + (location.search || '') }} />;
  }
  return <>{children}</>;
}

export function ForbiddenPage() {
  const { user } = useAuth();
  const homePath = (user && LANDING_PATH[user.role]) || DEFAULT_LANDING_ROUTE;
  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: '60vh',
        padding: '48px 24px',
        textAlign: 'center',
      }}
    >
      <div
        style={{
          width: 64,
          height: 64,
          borderRadius: '50%',
          background: '#FEF2F2',
          color: '#DC2626',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          marginBottom: 16,
          fontSize: 28,
          fontWeight: 800,
        }}
      >
        403
      </div>
      <h1 style={{ fontSize: '1.5rem', fontWeight: 800, color: '#0F172A', margin: '0 0 8px' }}>
        403 — Access denied
      </h1>
      <p style={{ fontSize: '0.95rem', color: '#64748B', maxWidth: 460, margin: '0 0 24px' }}>
        Your role does not have permission to view this page.
      </p>
      <a
        href={homePath}
        style={{
          padding: '10px 20px',
          background: '#0F172A',
          color: '#FFFFFF',
          borderRadius: 8,
          textDecoration: 'none',
          fontWeight: 600,
          fontSize: '0.9rem',
        }}
      >
        Return to Dashboard
      </a>
    </div>
  );
}

export function RoleGuard({ module, children }: { module: ModuleKey; children: React.ReactNode }) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  if (!getModule(module).roles[user.role]) {
    return <ForbiddenPage />;
  }
  return <>{children}</>;
}
