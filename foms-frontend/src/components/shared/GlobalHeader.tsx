import React, { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { Bell, User, LogOut, Sun, Moon } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { StatusBadge, RoleBadgeType } from './StatusBadge';

interface GlobalHeaderProps {
  title: string;
  subtitle?: string;
}

export const GlobalHeader: React.FC<GlobalHeaderProps> = ({ title, subtitle }) => {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [profileOpen, setProfileOpen] = useState(false);
  const [isDarkMode, setIsDarkMode] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  // Close dropdown on outside click
  useEffect(() => {
    const handleOutsideClick = (event: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setProfileOpen(false);
      }
    };
    document.addEventListener('mousedown', handleOutsideClick);
    return () => document.removeEventListener('mousedown', handleOutsideClick);
  }, []);

  const toggleDarkMode = () => {
    setIsDarkMode((prev) => {
      const next = !prev;
      if (next) {
        document.body.classList.add('dark');
        document.documentElement.classList.add('dark');
      } else {
        document.body.classList.remove('dark');
        document.documentElement.classList.remove('dark');
      }
      return next;
    });
  };

  const getUserInitials = () => {
    if (!user?.fullName) return 'SP';
    const parts = user.fullName.trim().split(/\s+/);
    if (parts.length >= 2) return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
    return parts[0].slice(0, 2).toUpperCase();
  };

  const getRoleBadge = (): RoleBadgeType => {
    switch (user?.role) {
      case 'FINANCIAL_MANAGER':
        return 'financial_manager';
      case 'HEAD_ACCOUNTANT':
        return 'head_accountant';
      case 'ACCOUNTANT':
        return 'accountant';
      case 'ASSISTANT_FM':
        return 'assistant_fm';
      case 'CLIENT':
        return 'client';
      default:
        return 'admin';
    }
  };

  return (
    <header
      style={{
        height: 'var(--topbar-height)',
        background: 'var(--bg-card)',
        borderBottom: '1px solid var(--border)',
        padding: '0 var(--padding-main)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        position: 'sticky',
        top: 0,
        zIndex: 'var(--z-sticky)',
        transition: 'background var(--motion-standard) var(--ease-standard), border-color var(--motion-standard) var(--ease-standard)',
      }}
      data-testid="global-header"
    >
      {/* Left: Page Title Only */}
      <div>
        <h1
          style={{
            fontSize: '1.45rem',
            fontWeight: 800,
            color: 'var(--text-primary)',
            letterSpacing: '-0.5px',
            margin: 0,
            lineHeight: 1.2,
          }}
        >
          {title}
        </h1>
        {subtitle && (
          <p
            style={{
              fontSize: '0.8rem',
              color: 'var(--text-muted)',
              margin: '2px 0 0',
              fontWeight: 500,
            }}
          >
            {subtitle}
          </p>
        )}
      </div>

      {/* Right: Notifications & Initials Dropdown */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
        {/* Dark Mode Toggle */}
        <button
          type="button"
          onClick={toggleDarkMode}
          aria-label={isDarkMode ? 'Switch to light mode' : 'Switch to dark mode'}
          style={{
            width: 36,
            height: 36,
            borderRadius: 'var(--radius-sm)',
            border: '1px solid var(--border)',
            background: 'var(--bg-input)',
            color: 'var(--text-secondary)',
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            transition: 'var(--transition)',
          }}
        >
          {isDarkMode ? <Sun size={18} strokeWidth={2} /> : <Moon size={18} strokeWidth={2} />}
        </button>

        {/* Notifications Bell */}
        <button
          type="button"
          onClick={() => navigate('/notifications')}
          aria-label="View notifications"
          style={{
            position: 'relative',
            width: 36,
            height: 36,
            borderRadius: 'var(--radius-sm)',
            border: '1px solid var(--border)',
            background: 'var(--bg-input)',
            color: 'var(--text-secondary)',
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            transition: 'var(--transition)',
          }}
        >
          <Bell size={20} strokeWidth={1.75} />
          {/* Unread Alert Dot */}
          <span
            style={{
              position: 'absolute',
              top: 5,
              right: 5,
              width: 8,
              height: 8,
              borderRadius: '50%',
              background: 'var(--status-failed)',
            }}
          />
        </button>

        {/* User Initials Avatar & Dropdown */}
        <div ref={dropdownRef} style={{ position: 'relative' }}>
          <button
            type="button"
            onClick={() => setProfileOpen((prev) => !prev)}
            aria-expanded={profileOpen}
            aria-label="User account menu"
            style={{
              width: 36,
              height: 36,
              borderRadius: 'var(--radius-sm)',
              background: 'var(--primary-dark)',
              color: '#FFFFFF',
              border: 'none',
              cursor: 'pointer',
              fontWeight: 700,
              fontSize: '0.85rem',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              transition: 'var(--transition)',
            }}
          >
            {getUserInitials()}
          </button>

          {profileOpen && (
            <div
              style={{
                position: 'absolute',
                top: 44,
                right: 0,
                width: 220,
                background: 'var(--bg-card)',
                border: '1px solid var(--border)',
                borderRadius: 'var(--radius-md)',
                boxShadow: 'var(--shadow-lg)',
                zIndex: 'var(--z-dropdown)',
                overflow: 'hidden',
                animation: 'fadeIn 0.2s ease-out',
              }}
            >
              <div
                style={{
                  padding: '12px 16px',
                  borderBottom: '1px solid var(--border)',
                  background: 'var(--bg-body)',
                }}
              >
                <div
                  style={{
                    fontSize: '0.85rem',
                    fontWeight: 700,
                    color: 'var(--text-primary)',
                    whiteSpace: 'nowrap',
                    textOverflow: 'ellipsis',
                    overflow: 'hidden',
                  }}
                >
                  {user?.fullName || 'Speedex Staff'}
                </div>
                <div style={{ marginTop: 4 }}>
                  <StatusBadge role={getRoleBadge()} status={user?.role || ''} />
                </div>
              </div>

              <div style={{ padding: '4px 0' }}>
                <button
                  type="button"
                  onClick={() => {
                    setProfileOpen(false);
                    navigate('/profile');
                  }}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 10,
                    width: '100%',
                    padding: '10px 16px',
                    fontSize: '0.85rem',
                    color: 'var(--text-secondary)',
                    background: 'none',
                    border: 'none',
                    textAlign: 'left',
                    cursor: 'pointer',
                    transition: 'var(--transition)',
                  }}
                  onMouseEnter={(e) => {
                    e.currentTarget.style.backgroundColor = 'var(--bg-body)';
                    e.currentTarget.style.color = 'var(--text-primary)';
                  }}
                  onMouseLeave={(e) => {
                    e.currentTarget.style.backgroundColor = 'transparent';
                    e.currentTarget.style.color = 'var(--text-secondary)';
                  }}
                >
                  <User size={16} strokeWidth={2} />
                  My Profile
                </button>

                <button
                  type="button"
                  onClick={() => {
                    setProfileOpen(false);
                    logout();
                  }}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 10,
                    width: '100%',
                    padding: '10px 16px',
                    fontSize: '0.85rem',
                    color: 'var(--status-failed)',
                    background: 'none',
                    border: 'none',
                    textAlign: 'left',
                    cursor: 'pointer',
                    transition: 'var(--transition)',
                  }}
                  onMouseEnter={(e) => {
                    e.currentTarget.style.backgroundColor = 'rgba(220, 38, 38, 0.05)';
                  }}
                  onMouseLeave={(e) => {
                    e.currentTarget.style.backgroundColor = 'transparent';
                  }}
                >
                  <LogOut size={16} strokeWidth={2} />
                  Log Out
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </header>
  );
};

export default GlobalHeader;
