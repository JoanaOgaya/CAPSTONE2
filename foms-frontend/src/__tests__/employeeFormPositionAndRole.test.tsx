import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ProfilePage } from '../pages/ProfilePage';
import { ToastProvider } from '../components/ToastContext';
import * as AuthContextModule from '../context/AuthContext';
import { usersService } from '../services/usersService';
import { auditLogsService } from '../services/auditLogsService';

vi.mock('../services/usersService', () => ({
  usersService: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    setStatus: vi.fn(),
  },
}));

vi.mock('../services/auditLogsService', () => ({
  auditLogsService: {
    list: vi.fn(),
  },
}));

const MOCK_ADMIN_USER = {
  id: 'EMP-001',
  employeeId: 'EMP-001',
  fullName: 'Crystalyn Joyce C. Fajardo',
  email: 'crystalyn@foms.local',
  role: 'Finance Manager',
  status: 'Active' as const,
};

describe('Add Employee Form: Position & Role Integration Tests (Phase 13)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
      user: MOCK_ADMIN_USER,
      token: 'mock-token',
      isAuthenticated: true,
      login: vi.fn(),
      logout: vi.fn(),
    });

    vi.mocked(usersService.list).mockResolvedValue([
      {
        id: 'EMP-001',
        employeeNumber: 'EMP-001',
        fullName: 'Crystalyn Joyce C. Fajardo',
        email: 'crystalyn@foms.local',
        role: 'Finance Manager',
        position: 'Finance Manager',
        status: 'Active',
      },
    ]);

    vi.mocked(auditLogsService.list).mockResolvedValue({ items: [] } as any);
  });

  it('1. Opens the Add Employee modal with empty Position and empty Role (no default Accountant)', async () => {
    render(
      <ToastProvider>
        <ProfilePage />
      </ToastProvider>
    );

    const addBtn = await screen.findByRole('button', { name: /Add Staff Member/i });
    fireEvent.click(addBtn);

    expect(screen.getByText('Register New Personnel')).toBeTruthy();

    const positionSelect = screen.getByRole('combobox') as HTMLSelectElement;
    expect(positionSelect.value).toBe('');

    const roleInput = screen.getByPlaceholderText('Derived from Position') as HTMLInputElement;
    expect(roleInput.value).toBe('');
    expect(roleInput.disabled).toBe(true);
  });

  it('2. Position dropdown contains the four finance positions and NO CEO or President option', async () => {
    render(
      <ToastProvider>
        <ProfilePage />
      </ToastProvider>
    );

    const addBtn = await screen.findByRole('button', { name: /Add Staff Member/i });
    fireEvent.click(addBtn);

    const positionSelect = screen.getByRole('combobox') as HTMLSelectElement;
    const options = Array.from(positionSelect.options).map(o => o.value);

    expect(options).toContain('');
    expect(options).toContain('Finance Manager');
    expect(options).toContain('Head Accountant');
    expect(options).toContain('Accountant');
    expect(options).toContain('Assistant of Finance Manager');

    // Verify CEO / President is strictly omitted
    expect(options).not.toContain('CEO');
    expect(options).not.toContain('President');
    expect(options).not.toContain('CEO/President');
  });

  it('3. Selecting a finance position automatically derives the FOMS Role as read-only', async () => {
    render(
      <ToastProvider>
        <ProfilePage />
      </ToastProvider>
    );

    const addBtn = await screen.findByRole('button', { name: /Add Staff Member/i });
    fireEvent.click(addBtn);

    const positionSelect = screen.getByRole('combobox') as HTMLSelectElement;
    const roleInput = screen.getByPlaceholderText('Derived from Position') as HTMLInputElement;

    // Select Assistant of Finance Manager
    fireEvent.change(positionSelect, { target: { value: 'Assistant of Finance Manager' } });
    expect(positionSelect.value).toBe('Assistant of Finance Manager');
    expect(roleInput.value).toBe('Assistant of Finance Manager');
    expect(roleInput.readOnly).toBe(true);

    // Change to Head Accountant
    fireEvent.change(positionSelect, { target: { value: 'Head Accountant' } });
    expect(positionSelect.value).toBe('Head Accountant');
    expect(roleInput.value).toBe('Head Accountant');
  });

  it('4. Form requires Position and rejects submission when Position is not selected', async () => {
    render(
      <ToastProvider>
        <ProfilePage />
      </ToastProvider>
    );

    const addBtn = await screen.findByRole('button', { name: /Add Staff Member/i });
    fireEvent.click(addBtn);

    fireEvent.change(screen.getByPlaceholderText('EMP-006'), { target: { value: 'EMP-009' } });
    fireEvent.change(screen.getByPlaceholderText('e.g. Maria Santos'), { target: { value: 'Test Personnel' } });
    fireEvent.change(screen.getByPlaceholderText('santos@foms.local'), { target: { value: 'test9@foms.local' } });

    const submitBtn = screen.getByRole('button', { name: /Create Employee/i }) as HTMLButtonElement;
    expect(submitBtn.disabled).toBe(true);
    expect(usersService.create).not.toHaveBeenCalled();
  });

  it('5. Submitting creates employee without hardcoded Password@123 and opens Temporary Credential modal', async () => {
    vi.mocked(usersService.create).mockResolvedValue({
      id: 'EMP-009',
      employeeNumber: 'EMP-009',
      fullName: 'Test Employee Nine',
      email: 'test9@foms.local',
      position: 'Accountant',
      role: 'Accountant',
      status: 'Active',
      temporaryPassword: 'GeneratedRandomPw#14',
    } as any);

    render(
      <ToastProvider>
        <ProfilePage />
      </ToastProvider>
    );

    const addBtn = await screen.findByRole('button', { name: /Add Staff Member/i });
    fireEvent.click(addBtn);

    fireEvent.change(screen.getByPlaceholderText('EMP-006'), { target: { value: 'EMP-009' } });
    fireEvent.change(screen.getByPlaceholderText('e.g. Maria Santos'), { target: { value: 'Test Employee Nine' } });
    fireEvent.change(screen.getByPlaceholderText('santos@foms.local'), { target: { value: 'test9@foms.local' } });

    const positionSelect = screen.getByRole('combobox');
    fireEvent.change(positionSelect, { target: { value: 'Accountant' } });

    const submitBtn = screen.getByRole('button', { name: /Create Employee/i }) as HTMLButtonElement;
    await waitFor(() => expect(submitBtn.disabled).toBe(false));

    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(usersService.create).toHaveBeenCalledTimes(1);
    });

    const createPayload = vi.mocked(usersService.create).mock.calls[0][0] as any;
    expect(createPayload.employeeId).toBe('EMP-009');
    expect(createPayload.position).toBe('Accountant');
    expect(createPayload.role).toBe('Accountant');
    // Ensure Password@123 was NOT passed in the payload
    expect(createPayload.password).toBeUndefined();

    // Verify the temporary password credential modal appears
    await waitFor(() => {
      expect(screen.getByText('Personnel Created')).toBeTruthy();
      expect(screen.getByDisplayValue('GeneratedRandomPw#14')).toBeTruthy();
      expect(screen.getByText(/The employee must change this password upon their first login/i)).toBeTruthy();
    });
  });
});
