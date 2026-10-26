import { useEffect, useRef, useCallback } from 'react';

const INACTIVITY_TIMEOUT_MS = 15 * 60 * 1000; // 15 minutes (IAM-02)

export function useInactivityLogout(onTimeout: () => void, isEnabled: boolean = true) {
  const timerRef = useRef<number | null>(null);

  const resetTimer = useCallback(() => {
    if (timerRef.current) {
      window.clearTimeout(timerRef.current);
    }
    if (isEnabled) {
      timerRef.current = window.setTimeout(() => {
        onTimeout();
      }, INACTIVITY_TIMEOUT_MS);
    }
  }, [onTimeout, isEnabled]);

  useEffect(() => {
    if (!isEnabled) return;

    const events = ['mousedown', 'keydown', 'scroll', 'touchstart', 'pointerdown'];
    const handleActivity = () => resetTimer();

    events.forEach((evt) => window.addEventListener(evt, handleActivity, { passive: true }));
    resetTimer();

    return () => {
      if (timerRef.current) {
        window.clearTimeout(timerRef.current);
      }
      events.forEach((evt) => window.removeEventListener(evt, handleActivity));
    };
  }, [resetTimer, isEnabled]);
}

export default useInactivityLogout;
