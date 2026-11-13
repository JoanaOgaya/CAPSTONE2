import React, { useRef, useEffect } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import Sidebar from './Sidebar';
import GlobalHeader from './GlobalHeader';
import { useRealtime } from '../hooks/useRealtime';

export const MainLayout: React.FC = () => {
  const mainAreaRef = useRef<HTMLDivElement>(null);
  const location = useLocation();

  // Reset scroll to top on page navigation
  useEffect(() => {
    mainAreaRef.current?.scrollTo(0, 0);
  }, [location.pathname]);

  // Realtime connection lives only while a session is mounted; logout unmounts it.
  useRealtime(true);

  return (
    <div className="app-layout" style={{ alignItems: 'stretch' }}>
      <Sidebar />
      <div
        ref={mainAreaRef}
        className="main-area"
        style={{
          justifyContent: 'flex-start',
          height: '100vh',
          overflowY: 'auto',
          overflowX: 'hidden',
          boxSizing: 'border-box',
        }}
      >
        <GlobalHeader />
        <main
          className="content-area"
          style={{
            flex: 1,
            padding: '28px',
            background: '#EEF2FF',
            display: 'flex',
            flexDirection: 'column',
            minWidth: 0,
            boxSizing: 'border-box',
            width: '100%',
            maxWidth: '100%',
          }}
        >
          <Outlet />
        </main>
      </div>
    </div>
  );
};

export default MainLayout;
