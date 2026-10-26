/**
 * starsAuthAdapter — Central STARS Authentication Adapter
 * Communicates with the configurable STARS central auth gateway.
 * When real endpoint is not yet connected or during development, uses the mock adapter
 * preserving existing test logins (EMP-001 through EMP-004) behind a dev flag.
 */

export interface StarsUser {
  userId: number | string;
  employeeNumber?: string;
  clientCode?: string;
  fullName: string;
  role: 'FINANCIAL_MANAGER' | 'HEAD_ACCOUNTANT' | 'ACCOUNTANT' | 'ASSISTANT_FM' | 'CLIENT';
  token: string;
  tokenExpiresAt: number; // Unix timestamp ms
}

const DEV_LOGINS: Record<string, Omit<StarsUser, 'token' | 'tokenExpiresAt'>> = {
  'EMP-001': {
    userId: 1,
    employeeNumber: 'EMP-001',
    fullName: 'Maria Santos (FM)',
    role: 'FINANCIAL_MANAGER',
  },
  'EMP-002': {
    userId: 2,
    employeeNumber: 'EMP-002',
    fullName: 'Ricardo Gomez (HA)',
    role: 'HEAD_ACCOUNTANT',
  },
  'EMP-003': {
    userId: 3,
    employeeNumber: 'EMP-003',
    fullName: 'Ana Reyes (Acc)',
    role: 'ACCOUNTANT',
  },
  'EMP-004': {
    userId: 4,
    employeeNumber: 'EMP-004',
    fullName: 'Carlos Ramos (AFM)',
    role: 'ASSISTANT_FM',
  },
  'CLI-001': {
    userId: 101,
    clientCode: 'CLI-001',
    fullName: 'Shopee Philippines Finance',
    role: 'CLIENT',
  },
};

export const StarsAuthService = {
  /** Check if development logins are enabled via environment variable */
  isDevLoginsEnabled(): boolean {
    return (
      import.meta.env.VITE_ENABLE_DEV_LOGINS !== 'false' &&
      (import.meta.env.MODE === 'development' || !import.meta.env.VITE_STARS_AUTH_URL)
    );
  },

  /** Login through STARS central endpoint with fallback to dev test accounts */
  async authenticate(identifier: string, password: string): Promise<StarsUser> {
    const starsAuthUrl = import.meta.env.VITE_STARS_AUTH_URL;

    // Real STARS integration attempt if endpoint is configured
    if (starsAuthUrl) {
      try {
        const response = await fetch(`${starsAuthUrl}/api/v1/auth/login`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ identifier, password, system: 'FOMS' }),
        });

        if (response.ok) {
          const data = await response.json();
          const expiresAt = Date.now() + 15 * 60 * 1000; // 15-minute validity (IAM-02)
          return {
            userId: data.userId,
            employeeNumber: data.employeeNumber,
            clientCode: data.clientCode,
            fullName: data.fullName,
            role: data.role,
            token: data.token,
            tokenExpiresAt: expiresAt,
          };
        }
      } catch (err) {
        console.warn('STARS central auth unreachable. Falling back to dev adapter.', err);
      }
    }

    // Dev test login fallback
    if (this.isDevLoginsEnabled()) {
      const normalizedKey = identifier.trim().toUpperCase();
      const mockUser = DEV_LOGINS[normalizedKey];

      if (mockUser && password.length >= 6) {
        const expiresAt = Date.now() + 15 * 60 * 1000; // 15 minutes (IAM-02)
        return {
          ...mockUser,
          token: `mock-stars-jwt-${mockUser.role.toLowerCase()}-${Date.now()}`,
          tokenExpiresAt: expiresAt,
        };
      }
    }

    throw new Error('Warning: Invalid employee ID or password. Please check your credentials.');
  },

  /** Check whether current session token is still valid within 15-min window */
  isSessionActive(tokenExpiresAt: number): boolean {
    return Date.now() < tokenExpiresAt;
  },
};

export default StarsAuthService;
