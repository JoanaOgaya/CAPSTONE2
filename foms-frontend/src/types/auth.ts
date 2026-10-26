/**
 * Auth types. Supports the 5 canonical system login roles per IAM-01:
 * Financial Manager, Head Accountant, Accountant, Assistant of Financial Manager, Client.
 */

export const ROLES = {
  FINANCIAL_MANAGER: 'FINANCIAL_MANAGER',
  HEAD_ACCOUNTANT: 'HEAD_ACCOUNTANT',
  ACCOUNTANT: 'ACCOUNTANT',
  ASSISTANT_FM: 'ASSISTANT_FM',
  CLIENT: 'CLIENT',
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];

export const Role = ROLES;

export interface User {
  id?: string;
  employeeId?: string;
  clientCode?: string;
  fullName: string;
  role: Role;
  email?: string;
}

export interface LoginCredentials {
  employeeId?: string;
  username?: string;
  password: string;
}

export interface AuthContextValue {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (credentials: LoginCredentials) => Promise<{ success: boolean; error?: string }>;
  logout: (options?: { reason?: string } | unknown) => void;
}
