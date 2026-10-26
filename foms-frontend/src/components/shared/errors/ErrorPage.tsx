import React from 'react';
import { useNavigate } from 'react-router-dom';

export type ErrorCode = 400 | 401 | 403 | 404 | 408 | 500 | 503;

interface ErrorPageProps {
  code?: ErrorCode;
  customTitle?: string;
  customDescription?: string;
  onRecover?: () => void;
}

const ERROR_CONFIG: Record<ErrorCode, { label: string; title: string; desc: string; action: string }> = {
  400: {
    label: 'Invalid Request',
    title: 'We could not process that request.',
    desc: 'The parameters provided were incomplete or invalid. Please return and verify the form entries.',
    action: 'Go Back',
  },
  401: {
    label: 'Session Expired',
    title: 'Your session has expired. Please log in again.',
    desc: 'For security purposes, inactive sessions automatically end after 15 minutes. Please log in to continue.',
    action: 'Log In',
  },
  403: {
    label: 'Access Restricted',
    title: 'You do not have permission to view this.',
    desc: 'Your current role does not include access to this section. Contact your system administrator if you believe this is an error.',
    action: 'Return to Dashboard',
  },
  404: {
    label: 'Page Not Found',
    title: 'This page could not be located.',
    desc: 'The page you are looking for may have been moved or the link may be incorrect. Return to the dashboard to continue.',
    action: 'Return to Dashboard',
  },
  408: {
    label: 'Request Timed Out',
    title: 'The connection took too long. Please check your internet and try again.',
    desc: 'The server did not receive a timely response. Please check your network connection and retry the operation.',
    action: 'Retry Request',
  },
  500: {
    label: 'System Error',
    title: 'Something went wrong on our end.',
    desc: 'The system encountered an unexpected issue. Your work has not been lost. Please wait a moment and try again, or contact IT support.',
    action: 'Try Again',
  },
  503: {
    label: 'System Unavailable',
    title: 'The system is temporarily unavailable. We are working to restore access.',
    desc: 'Maintenance or high network demand is currently affecting service. Please check back shortly.',
    action: 'Check System Status',
  },
};

export const ErrorPage: React.FC<ErrorPageProps> = ({
  code = 404,
  customTitle,
  customDescription,
  onRecover,
}) => {
  const navigate = useNavigate();
  const config = ERROR_CONFIG[code] || ERROR_CONFIG[404];

  const handleAction = () => {
    if (onRecover) {
      onRecover();
    } else if (code === 401) {
      navigate('/login');
    } else {
      navigate('/foms/dashboard');
    }
  };

  const codeStr = String(code);
  const firstDigit = codeStr[0];
  const midDigit = codeStr[1];
  const lastDigit = codeStr[2];

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: '60vh',
        padding: '56px 24px',
        textAlign: 'center',
        background: 'var(--bg-body)',
        borderRadius: 'var(--radius-md)',
        border: '1px solid var(--border)',
        gap: 16,
        margin: '24px auto',
        maxWidth: 600,
      }}
      data-testid={`error-page-${code}`}
    >
      <div
        style={{
          fontSize: '4.5rem',
          fontWeight: 800,
          color: 'var(--text-primary)',
          letterSpacing: '-2px',
          lineHeight: 1,
        }}
      >
        {firstDigit}
        <span style={{ color: 'var(--primary)' }}>{midDigit}</span>
        {lastDigit}
      </div>

      <div
        style={{
          fontSize: '0.75rem',
          fontWeight: 700,
          textTransform: 'uppercase',
          letterSpacing: '1px',
          color: 'var(--text-muted)',
        }}
      >
        {config.label}
      </div>

      <h2
        style={{
          fontSize: '1.25rem',
          fontWeight: 700,
          color: 'var(--text-primary)',
          margin: 0,
        }}
      >
        {customTitle || config.title}
      </h2>

      <p
        style={{
          fontSize: '0.88rem',
          color: 'var(--text-secondary)',
          maxWidth: 420,
          lineHeight: 1.6,
          margin: 0,
        }}
      >
        {customDescription || config.desc}
      </p>

      <button
        type="button"
        onClick={handleAction}
        className="btn btn-primary"
        style={{ marginTop: 12 }}
      >
        {config.action}
      </button>
    </div>
  );
};

export default ErrorPage;
