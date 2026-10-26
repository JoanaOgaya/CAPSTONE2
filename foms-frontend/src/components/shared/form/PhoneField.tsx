import React from 'react';
import { TextField } from './TextField';

export interface PhoneFieldProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'onChange'> {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  success?: string;
  hint?: string;
}

export const PhoneField: React.FC<PhoneFieldProps> = ({
  label,
  value,
  onChange,
  error,
  success,
  hint = 'Enter 11-digit mobile number starting with 09 (e.g. 09171234567)',
  ...props
}) => {
  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const raw = e.target.value.replace(/\D/g, '').slice(0, 11);
    onChange(raw);
  };

  const computedError =
    error ||
    (value && value.length > 0 && (!value.startsWith('09') || value.length < 11)
      ? '⚠ Contact number must be 11 digits starting with 09.'
      : undefined);

  const computedSuccess =
    success || (value && value.length === 11 && value.startsWith('09') ? '✓ Contact number confirmed.' : undefined);

  return (
    <TextField
      label={label}
      type="tel"
      value={value}
      onChange={handleChange}
      placeholder="09XXXXXXXXX"
      maxLength={11}
      error={computedError}
      success={computedSuccess}
      hint={hint}
      {...props}
    />
  );
};

export default PhoneField;
