import React, { useState } from 'react';
import { AlertTriangle, Lock, X } from 'lucide-react';

export interface ConfirmModalProps {
  isOpen: boolean;
  onClose?: () => void;
  onConfirm: () => void | Promise<void>;
  title: string;
  affectedItem?: string;
  actionDescription?: string;
  confirmButtonText?: string;
  requirePasscode?: boolean;
  expectedPasscode?: string;
  isDanger?: boolean;

  // Legacy component prop aliases for unmigrated pages
  message?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: string;
  children?: React.ReactNode;
  onCancel?: () => void;
  icon?: string;
}

export const ConfirmModal: React.FC<ConfirmModalProps> = ({
  isOpen,
  onClose,
  onConfirm,
  title,
  affectedItem,
  actionDescription,
  confirmButtonText,
  requirePasscode = false,
  expectedPasscode = 'CONFIRM',
  isDanger,

  // Legacy aliases
  message,
  confirmLabel,
  cancelLabel = 'Cancel',
  variant,
  children,
  onCancel,
}) => {
  const [passcode, setPasscode] = useState('');
  const [errorMsg, setErrorMsg] = useState('');

  if (!isOpen) return null;

  const resolvedClose = onClose || onCancel || (() => {});
  const resolvedConfirmBtnText = confirmButtonText || confirmLabel || 'Confirm Action';
  const resolvedDescription = actionDescription || message || '';
  const resolvedIsDanger = isDanger !== undefined ? isDanger : variant === 'danger';

  const handleConfirm = async () => {
    if (requirePasscode && passcode.trim().toUpperCase() !== expectedPasscode.toUpperCase()) {
      setErrorMsg(`Warning: Passcode verification failed. Please enter "${expectedPasscode}" to proceed.`);
      return;
    }
    setErrorMsg('');
    setPasscode('');
    await onConfirm();
  };

  const handleClose = () => {
    setErrorMsg('');
    setPasscode('');
    resolvedClose();
  };

  return (
    <div
      style={{
        position: 'fixed',
        top: 0,
        left: 0,
        width: '100vw',
        height: '100vh',
        background: 'rgba(15, 23, 42, 0.65)',
        backdropFilter: 'blur(3px)',
        zIndex: 'var(--z-modal)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 20,
      }}
      role="dialog"
      aria-modal="true"
      data-testid="confirm-modal"
    >
      <div
        style={{
          background: 'var(--bg-card)',
          borderRadius: 'var(--radius-md)',
          width: '100%',
          maxWidth: '460px',
          boxShadow: 'var(--shadow-lg)',
          border: resolvedIsDanger ? '2px solid var(--status-failed)' : '1px solid var(--border)',
          overflow: 'hidden',
          animation: 'fadeIn 0.2s ease-out',
        }}
      >
        {/* Red / Accent Header */}
        <div
          style={{
            background: resolvedIsDanger ? 'rgba(220, 38, 38, 0.08)' : 'rgba(0, 169, 157, 0.08)',
            padding: '16px 20px',
            borderBottom: '1px solid var(--border)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            {resolvedIsDanger ? (
              <AlertTriangle size={20} color="var(--status-failed)" strokeWidth={2} />
            ) : (
              <Lock size={20} color="var(--primary)" strokeWidth={2} />
            )}
            <h3
              style={{
                fontSize: '1.05rem',
                fontWeight: 700,
                color: resolvedIsDanger ? 'var(--status-failed)' : 'var(--text-primary)',
                margin: 0,
              }}
            >
              {title}
            </h3>
          </div>
          <button
            onClick={handleClose}
            style={{
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              color: 'var(--text-secondary)',
              display: 'flex',
              padding: 4,
            }}
            aria-label="Close modal"
          >
            <X size={18} />
          </button>
        </div>

        {/* Modal Body */}
        <div style={{ padding: '20px 24px' }}>
          {resolvedDescription && (
            <p style={{ fontSize: '0.88rem', color: 'var(--text-secondary)', marginBottom: 12 }}>
              {resolvedDescription}
            </p>
          )}

          {children}

          {affectedItem && (
            <div
              style={{
                background: 'var(--bg-body)',
                padding: '12px 16px',
                borderRadius: 'var(--radius-sm)',
                border: '1px solid var(--border)',
                marginBottom: 16,
                fontSize: '0.9rem',
              }}
            >
              <span style={{ color: 'var(--text-muted)', fontSize: '0.78rem', display: 'block', textTransform: 'uppercase', fontWeight: 700 }}>
                Affected Record
              </span>
              <strong style={{ color: 'var(--text-primary)', fontSize: '1rem' }}>
                {affectedItem}
              </strong>
            </div>
          )}

          {requirePasscode && (
            <div style={{ marginBottom: 16, marginTop: 12 }}>
              <label
                htmlFor="confirm-passcode-input"
                style={{
                  display: 'block',
                  fontSize: '0.75rem',
                  fontWeight: 700,
                  color: 'var(--text-primary)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.5px',
                  marginBottom: 6,
                }}
              >
                Verification: Type "{expectedPasscode}" to confirm
              </label>
              <input
                id="confirm-passcode-input"
                type="text"
                value={passcode}
                onChange={(e) => {
                  setPasscode(e.target.value);
                  if (errorMsg) setErrorMsg('');
                }}
                placeholder={expectedPasscode}
                className="form-input"
                style={{
                  width: '100%',
                  height: 38,
                  padding: '0 12px',
                  border: errorMsg ? '1.5px solid var(--status-failed)' : '1.5px solid var(--border)',
                  borderRadius: 'var(--radius-sm)',
                  background: 'var(--bg-input)',
                  fontSize: '0.9rem',
                  outline: 'none',
                }}
              />
              {errorMsg && (
                <span
                  style={{
                    display: 'block',
                    color: 'var(--status-failed)',
                    fontSize: '0.8rem',
                    fontWeight: 600,
                    marginTop: 6,
                  }}
                >
                  {errorMsg}
                </span>
              )}
            </div>
          )}

          {/* Action Buttons */}
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 20 }}>
            <button
              type="button"
              onClick={handleClose}
              className="btn btn-outline"
            >
              {cancelLabel}
            </button>
            <button
              type="button"
              onClick={handleConfirm}
              className={`btn ${resolvedIsDanger ? 'btn-danger' : 'btn-primary'}`}
            >
              {resolvedConfirmBtnText}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ConfirmModal;
