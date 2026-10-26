import React from 'react';
import { DashboardLayoutShell } from './shared/DashboardLayoutShell';
import { useRealtime } from '../hooks/useRealtime';

export const MainLayout: React.FC = () => {
  // Realtime connection lives only while a session is mounted; logout unmounts it.
  useRealtime(true);

  return <DashboardLayoutShell />;
};

export default MainLayout;
