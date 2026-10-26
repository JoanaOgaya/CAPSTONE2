/**
 * AuthContext — session state only (current user, login, logout).
 * The user and role always come from GET /auth/me; no business data lives here.
 * Logout / 401 / inactivity clear the token, the query cache and the realtime connection (via route unmount).
 */
import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import type { AuthContextValue, LoginCredentials, User } from '../types/auth';
import { authService } from '../services/authService';
import { onUnauthorized, session } from '../services/apiClient';
import { clearQueryCache } from '../hooks/useQuery';
import { riskService } from '../services/ai/risk.service';
import { LANDING_PATH } from '../utils/roleAccess';
import { bootstrapIncomingAuth, clearStoredSession, getCentralLoginUrl, getInitialProfile, getSystemPortalUrl, isSuperAdminId } from '../services/centralAuth';

// Bootstrap any SSO auth params returned when entering FOMS from Speedex Central Portal
bootstrapIncomingAuth();

const INACTIVITY_TIMEOUT_MS = Number(import.meta.env.VITE_INACTIVITY_TIMEOUT_MS) || 30 * 60 * 1000;
const ACTIVITY_EVENTS: (keyof WindowEventMap)[] = ['mousemove', 'keydown', 'click', 'scroll'];

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate();
  const location = useLocation();
  const [user, setUser] = useState<User | null>(getInitialProfile);
  const [isLoading, setIsLoading] = useState<boolean>(() => !getInitialProfile() && !!session.get());
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const endSession = useCallback((reason: string = 'logged_out') => {
    let authDataToHandoff: any = null;
    let allowedSystemsVal = '';
    try {
      const sessionRaw = localStorage.getItem('foms_session') || sessionStorage.getItem('foms_session');
      const profileRaw = localStorage.getItem('foms_user_profile');
      const storedAllowed = localStorage.getItem('foms_allowed_systems');
      if (sessionRaw) {
        const parsedSession = JSON.parse(sessionRaw);
        const parsedProfile = profileRaw ? JSON.parse(profileRaw) : null;
        authDataToHandoff = {
          ...parsedSession,
          ...parsedProfile,
        };
      }
      if (storedAllowed) {
        allowedSystemsVal = storedAllowed;
      }
    } catch {
      // storage safeguard
    }

    const employeeId = authDataToHandoff?.employeeId || user?.employeeId || '';
    const isSuper = isSuperAdminId(employeeId);

    if (isSuper) {
      allowedSystemsVal = 'STARS,DMS,FOMS';
    } else if (!allowedSystemsVal) {
      // Check token claims if storedAllowed was missing
      if (authDataToHandoff?.accessToken) {
        try {
          const parts = authDataToHandoff.accessToken.split('.');
          if (parts.length === 3) {
            const payload = JSON.parse(atob(parts[1].replace(/-/g, '+').replace(/_/g, '/')));
            const claimSystems = payload.allowedSystems || payload.systems;
            if (claimSystems && typeof claimSystems === 'string' && claimSystems.includes(',')) {
              allowedSystemsVal = claimSystems;
            }
          }
        } catch {
          // ignore
        }
      }
      if (!allowedSystemsVal) {
        allowedSystemsVal = 'FOMS';
      }
    }

    clearStoredSession();
    clearQueryCache();
    riskService.clearCache();
    setUser(null);

    const systemsList = allowedSystemsVal
      .split(',')
      .map((s) => s.trim().toUpperCase())
      .filter(Boolean);

    const isSingleSystem = !isSuper && systemsList.length <= 1;

    if (reason === 'inactivity' || reason === 'session_expired') {
      const loginUrl = getCentralLoginUrl();
      if (typeof window !== 'undefined') {
        window.location.href = `${loginUrl}?action=logout&reason=${encodeURIComponent(reason)}&system=foms`;
      }
    } else if (isSingleSystem) {
      // Accounts tied strictly to ONLY FOMS must go directly to Central Authentication Login page upon logout
      const loginUrl = getCentralLoginUrl();
      if (typeof window !== 'undefined') {
        window.location.replace(`${loginUrl}?action=logout&reason=logged_out&system=foms`);
      }
    } else {
      // Accounts tied to MORE than one system return to the Central Landing Page (/portal) so they can switch systems
      const portalUrl = getSystemPortalUrl();
      if (typeof window !== 'undefined') {
        if (authDataToHandoff?.accessToken) {
          const handoffId = isSuper ? '0000' : employeeId;
          const hashFragment = `token=${encodeURIComponent(authDataToHandoff.accessToken)}&refreshToken=${encodeURIComponent(authDataToHandoff.refreshToken || '')}&role=${encodeURIComponent(authDataToHandoff.role || '')}&employeeId=${encodeURIComponent(handoffId)}&employeeName=${encodeURIComponent(authDataToHandoff.fullName || authDataToHandoff.name || '')}&allowedSystems=${encodeURIComponent(systemsList.join(','))}&system=foms`;
          window.location.replace(`${portalUrl}#${hashFragment}`);
        } else {
          window.location.replace(`${portalUrl}?system=foms`);
        }
      }
    }
  }, [user]);

  const logout = useCallback((options?: { reason?: string } | unknown) => {
    const reason = (options && typeof options === 'object' && 'reason' in (options as Record<string, unknown>) && typeof (options as Record<string, unknown>).reason === 'string')
      ? (options as { reason: string }).reason
      : 'logged_out';

    try {
      sessionStorage.setItem('loggedOut', 'true');
    } catch {
      // storage access safeguard
    }
    const currentRefreshToken = session.get()?.refreshToken;
    authService.logout(currentRefreshToken).catch(() => undefined).finally(() => endSession(reason));
  }, [endSession]);

  // Restore session from the server (updates cached profile with fresh backend data)
  useEffect(() => {
    const currentSession = session.get();
    if (!currentSession?.accessToken) {
      setIsLoading(false);
      return;
    }

    const restoreSession = async () => {
      try {
        const me = await authService.me();
        setUser(me);
        try {
          localStorage.setItem('foms_user_profile', JSON.stringify(me));
        } catch {
          // storage safeguard
        }
      } catch (err: any) {
        // If 401 Unauthorized, attempt SSO token exchange with the stored token
        const isAuthError = err?.status === 401 || err?.response?.status === 401;
        if (isAuthError && currentSession.accessToken) {
          try {
            const exchangeRes = await authService.ssoExchange(currentSession.accessToken);
            if (exchangeRes?.accessToken) {
              session.set({
                accessToken: exchangeRes.accessToken,
                refreshToken: exchangeRes.refreshToken || currentSession.refreshToken,
              });
              const meAfterExchange = await authService.me();
              setUser(meAfterExchange);
              try {
                localStorage.setItem('foms_user_profile', JSON.stringify(meAfterExchange));
              } catch {
                // storage safeguard
              }
              return;
            }
          } catch (exchangeErr) {
            console.warn('SSO token exchange failed in FOMS:', exchangeErr);
          }
        }

        console.warn('Initial FOMS profile verification failed or server warming up. Retaining SSO profile.', err);
        // Matching DMS reference: Do NOT clear stored session or wipe user if we already have an active profile from SSO
        if (!getInitialProfile()) {
          clearStoredSession();
          setUser(null);
        }
      } finally {
        setIsLoading(false);
      }
    };

    void restoreSession();
  }, []);

  useEffect(() => {
    onUnauthorized(() => {
      endSession('session_expired');
    });
    return () => onUnauthorized(null);
  }, [endSession]);

  // User switch: never carry one user's cached data into another session.
  const userId = user?.employeeId;
  useEffect(() => () => clearQueryCache(), [userId]);

  // Inactivity auto-logout (Retaining 30-minute window per business requirements)
  useEffect(() => {
    if (!user) return undefined;
    const reset = () => {
      if (timer.current) clearTimeout(timer.current);
      timer.current = setTimeout(() => logout({ reason: 'inactivity' }), INACTIVITY_TIMEOUT_MS);
    };
    ACTIVITY_EVENTS.forEach((e) => window.addEventListener(e, reset));
    reset();
    return () => {
      ACTIVITY_EVENTS.forEach((e) => window.removeEventListener(e, reset));
      if (timer.current) clearTimeout(timer.current);
    };
  }, [user, logout]);

  const login = useCallback(async ({ employeeId, password }: LoginCredentials) => {
    setIsLoading(true);
    try {
      const tokens = await authService.login(employeeId, password);
      session.set({ accessToken: tokens.accessToken, refreshToken: tokens.refreshToken });
      const me = await authService.me();
      setUser(me);
      try {
        localStorage.setItem('foms_user_profile', JSON.stringify(me));
        if (isSuperAdminId(employeeId) || isSuperAdminId(me.employeeId)) {
          localStorage.setItem('foms_allowed_systems', 'STARS,DMS,FOMS');
        }
      } catch {
        // storage safeguard
      }

      let requestedFrom: string | undefined;
      const wasLoggedOut = typeof window !== 'undefined' && sessionStorage.getItem('loggedOut') === 'true';
      if (typeof window !== 'undefined') {
        sessionStorage.removeItem('loggedOut');
      }

      if (!wasLoggedOut) {
        const state = location.state as { from?: string } | null;
        if (state?.from && state.from !== '/login' && state.from !== '/') {
          requestedFrom = state.from;
        }
      }

      const target = requestedFrom || LANDING_PATH[me.role];
      navigate(target, { replace: true });
      return { success: true };
    } catch (e) {
      clearStoredSession();
      return { success: false, error: e instanceof Error ? e.message : 'Unable to sign in.' };
    } finally {
      setIsLoading(false);
    }
  }, [navigate, location]);

  const value = useMemo<AuthContextValue>(
    () => ({ user, isAuthenticated: !!user, isLoading, login, logout }),
    [user, isLoading, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
}
