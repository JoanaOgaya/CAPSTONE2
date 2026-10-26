import React from 'react';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider, useAuth } from '../context/AuthContext';
import { MemoryRouter } from 'react-router-dom';
import { authService } from '../services/authService';

vi.mock('../services/authService', () => ({
  authService: {
    me: vi.fn().mockResolvedValue({
      id: 'EMP-001',
      employeeId: 'EMP-001',
      fullName: 'Crystalyn Joyce C. Fajardo',
      role: 'FINANCIALMANAGER',
      email: 'finance.manager@speedex.test',
    }),
    login: vi.fn(),
    logout: vi.fn().mockResolvedValue(undefined),
    ssoExchange: vi.fn(),
  },
}));

vi.mock('../services/realtime', () => ({
  realtime: {
    connect: vi.fn(),
    disconnect: vi.fn(),
    on: vi.fn(),
    off: vi.fn(),
  },
}));

const LogoutTestComponent: React.FC = () => {
  const { user, logout } = useAuth();
  return (
    <div>
      <span data-testid="user-info">{user?.fullName || 'No User'}</span>
      <button data-testid="logout-btn" onClick={() => logout()}>
        Log Out
      </button>
      <button data-testid="inactivity-btn" onClick={() => logout({ reason: 'inactivity' })}>
        Inactivity Timeout
      </button>
    </div>
  );
};

describe('FOMS Logout Routing & Single vs Multi-System Isolation', () => {
  const originalLocation = window.location;

  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    sessionStorage.clear();

    // Mock window.location
    const mockLocation = {
      href: 'http://localhost:5174/dashboard',
      pathname: '/dashboard',
      search: '',
      hash: '',
      replace: vi.fn(),
    } as unknown as Location;

    Object.defineProperty(window, 'location', {
      writable: true,
      configurable: true,
      value: mockLocation,
    });
  });

  afterEach(() => {
    Object.defineProperty(window, 'location', {
      writable: true,
      configurable: true,
      value: originalLocation,
    });
  });

  it('1. Single-system FOMS account redirects directly to Central Login upon voluntary logout', async () => {
    // Seed single-system session
    const mockSession = { accessToken: 'test-foms-jwt-token', refreshToken: 'test-foms-refresh-token' };
    const mockProfile = {
      id: 'EMP-001',
      employeeId: 'EMP-001',
      fullName: 'Crystalyn Joyce C. Fajardo',
      role: 'FINANCIALMANAGER',
      email: 'finance.manager@speedex.test',
    };
    localStorage.setItem('foms_session', JSON.stringify(mockSession));
    localStorage.setItem('foms_user_profile', JSON.stringify(mockProfile));
    localStorage.setItem('foms_allowed_systems', 'FOMS');

    render(
      <MemoryRouter>
        <AuthProvider>
          <LogoutTestComponent />
        </AuthProvider>
      </MemoryRouter>
    );

    const logoutBtn = screen.getByTestId('logout-btn');
    fireEvent.click(logoutBtn);

    await waitFor(() => {
      expect(window.location.replace).toHaveBeenCalled();
    });

    const replacedUrl = (window.location.replace as any).mock.calls[0][0];
    // Must go directly to central login (/login?action=logout...)
    expect(replacedUrl).toContain('/login?action=logout');
    expect(replacedUrl).toContain('system=foms');
    expect(replacedUrl).not.toContain('/portal');
    expect(replacedUrl).not.toContain('stars-two-chi');

    // Storage must be cleared
    expect(localStorage.getItem('foms_session')).toBeNull();
    expect(localStorage.getItem('foms_user_profile')).toBeNull();
    expect(localStorage.getItem('foms_allowed_systems')).toBeNull();
  });

  it('2. Multi-system account (STARS,FOMS) redirects to Central Landing Page (/portal) upon voluntary logout', async () => {
    // Seed multi-system session
    const mockSession = { accessToken: 'test-multi-jwt-token', refreshToken: 'test-multi-refresh-token' };
    const mockProfile = {
      id: 'EMP-001',
      employeeId: 'EMP-001',
      fullName: 'Crystalyn Joyce C. Fajardo',
      role: 'FINANCIALMANAGER',
      email: 'finance.manager@speedex.test',
    };
    localStorage.setItem('foms_session', JSON.stringify(mockSession));
    localStorage.setItem('foms_user_profile', JSON.stringify(mockProfile));
    localStorage.setItem('foms_allowed_systems', 'STARS,FOMS');

    render(
      <MemoryRouter>
        <AuthProvider>
          <LogoutTestComponent />
        </AuthProvider>
      </MemoryRouter>
    );

    const logoutBtn = screen.getByTestId('logout-btn');
    fireEvent.click(logoutBtn);

    await waitFor(() => {
      expect(window.location.replace).toHaveBeenCalled();
    });

    const replacedUrl = (window.location.replace as any).mock.calls[0][0];
    // Must go to portal (/portal#token=...&allowedSystems=STARS%2CFOMS&system=foms)
    expect(replacedUrl).toContain('/portal#token=');
    expect(replacedUrl).toContain('allowedSystems=STARS%2CFOMS');
    expect(replacedUrl).toContain('system=foms');
    expect(replacedUrl).not.toContain('stars-two-chi.vercel.app');
  });

  it('3. Inactivity timeout redirects directly to Central Login with reason=inactivity', async () => {
    const mockSession = { accessToken: 'test-token', refreshToken: 'test-refresh' };
    const mockProfile = {
      id: 'EMP-001',
      employeeId: 'EMP-001',
      fullName: 'Crystalyn Joyce C. Fajardo',
      role: 'FINANCIALMANAGER',
    };
    localStorage.setItem('foms_session', JSON.stringify(mockSession));
    localStorage.setItem('foms_user_profile', JSON.stringify(mockProfile));
    localStorage.setItem('foms_allowed_systems', 'FOMS');

    render(
      <MemoryRouter>
        <AuthProvider>
          <LogoutTestComponent />
        </AuthProvider>
      </MemoryRouter>
    );

    const inactivityBtn = screen.getByTestId('inactivity-btn');
    fireEvent.click(inactivityBtn);

    await waitFor(() => {
      expect(window.location.href).toContain('/login?action=logout&reason=inactivity&system=foms');
    });
  });

  it('4. Root SuperAdmin (0000) redirects to Central Landing Page (/portal) with canonical employeeId upon voluntary logout', async () => {
    const mockSession = { accessToken: 'test-admin-jwt-token', refreshToken: 'test-admin-refresh-token' };
    const mockProfile = {
      id: '0000',
      employeeId: '0000',
      fullName: 'Root SuperAdmin',
      role: 'FINANCIALMANAGER' as any,
      email: 'admin@speedex.com',
    };
    vi.mocked(authService.me).mockResolvedValueOnce(mockProfile);
    localStorage.setItem('foms_session', JSON.stringify(mockSession));
    localStorage.setItem('foms_user_profile', JSON.stringify(mockProfile));
    localStorage.setItem('foms_allowed_systems', 'STARS,DMS,FOMS');

    render(
      <MemoryRouter>
        <AuthProvider>
          <LogoutTestComponent />
        </AuthProvider>
      </MemoryRouter>
    );

    const logoutBtn = screen.getByTestId('logout-btn');
    fireEvent.click(logoutBtn);

    await waitFor(() => {
      expect(window.location.replace).toHaveBeenCalled();
    });

    const replacedUrl = (window.location.replace as any).mock.calls[0][0];
    expect(replacedUrl).toContain('/portal#token=');
    expect(replacedUrl).toContain('employeeId=0000');
    expect(replacedUrl).toContain('allowedSystems=STARS%2CDMS%2CFOMS');
    expect(replacedUrl).not.toContain('/login?action=logout');
  });

  it('5. Root SuperAdmin with FOMS database normalized ID (EMP-000) redirects to /portal and does not fall back to single-system login', async () => {
    const mockSession = { accessToken: 'test-admin-foms-token', refreshToken: 'test-admin-refresh' };
    const mockProfile = {
      id: 'EMP-000',
      employeeId: 'EMP-000',
      fullName: 'Root SuperAdmin',
      role: 'FINANCIALMANAGER' as any,
      email: 'admin@speedex.com',
    };
    vi.mocked(authService.me).mockResolvedValueOnce(mockProfile);
    localStorage.setItem('foms_session', JSON.stringify(mockSession));
    localStorage.setItem('foms_user_profile', JSON.stringify(mockProfile));
    // Even if foms_allowed_systems is missing:
    localStorage.removeItem('foms_allowed_systems');

    render(
      <MemoryRouter>
        <AuthProvider>
          <LogoutTestComponent />
        </AuthProvider>
      </MemoryRouter>
    );

    const logoutBtn = screen.getByTestId('logout-btn');
    fireEvent.click(logoutBtn);

    await waitFor(() => {
      expect(window.location.replace).toHaveBeenCalled();
    });

    const replacedUrl = (window.location.replace as any).mock.calls[0][0];
    expect(replacedUrl).toContain('/portal#token=');
    expect(replacedUrl).toContain('employeeId=0000');
    expect(replacedUrl).toContain('allowedSystems=STARS%2CDMS%2CFOMS');
    expect(replacedUrl).not.toContain('/login?action=logout');
  });
});
