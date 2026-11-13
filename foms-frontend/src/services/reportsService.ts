import { http } from './apiClient';
import { createResource } from './resource';
import type { Report } from '../types/entities';

export const reportsService = {
  ...createResource<Report>('/reports'),
  /** Server-side report data for the given report type and filters. */
  generate: <T = unknown>(type: string, params?: object) => http.get<T>(`/reports/${type}`, params),
  exportExcel: (reportType: string, fileName: string, headers: string[], rows: unknown[][]) =>
    http.post<{ fileName: string; fileContentBase64: string }>('/reports/export/excel', { reportType, fileName, headers, rows }),
  logExport: (reportType: string, format: 'XLSX' | 'PDF', fileName: string, recordCount: number) =>
    http.post('/reports/export-audit', { reportType, format, fileName, recordCount }),
};
