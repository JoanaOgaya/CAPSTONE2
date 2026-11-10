import { http } from './apiClient';

export interface DepartmentDto {
  departmentId: number;
  departmentCode: string;
  departmentName: string;
  isActive: boolean;
  isOverhead: boolean;
  isLegacy: boolean;
}

export interface DepartmentSuggestionDto {
  departmentCode?: string | null;
  departmentName?: string | null;
  hasSuggestion: boolean;
}

export interface ChangeDepartmentPayload {
  requestType: 'CashAdvance' | 'DirectDisbursement' | 'Reimbursement';
  requestId: string;
  newDepartmentCode: string;
  reason: string;
}

export interface DepartmentChangeLogDto {
  logId: number;
  requestType: string;
  requestId: string;
  oldDepartmentCode: string;
  oldDepartmentName: string;
  newDepartmentCode: string;
  newDepartmentName: string;
  reason: string;
  changedBy: string;
  changedAt: string;
}

export const departmentsService = {
  async getDepartments(): Promise<DepartmentDto[]> {
    return http.get<DepartmentDto[]>('/api/departments');
  },

  async getDepartmentSuggestion(): Promise<DepartmentSuggestionDto> {
    return http.get<DepartmentSuggestionDto>('/api/departments/suggestion');
  },

  async changeDepartment(payload: ChangeDepartmentPayload): Promise<DepartmentChangeLogDto> {
    return http.post<DepartmentChangeLogDto>('/api/departments/change', payload);
  },

  async getChangeLogs(requestType: string, requestId: string): Promise<DepartmentChangeLogDto[]> {
    return http.get<DepartmentChangeLogDto[]>('/api/departments/change-logs', { requestType, requestId });
  },
};
