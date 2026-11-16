import { useState } from "react";

export default function PasswordInput({ id, ...inputProps }) {
  const [visible, setVisible] = useState(false);

  return (
    <div className="auth-password">
      <input {...inputProps} id={id} type={visible ? "text" : "password"} />
      <button
        type="button"
        className="auth-password__toggle"
        aria-label={`${visible ? "Ẩn" : "Hiện"} mật khẩu`}
        aria-controls={id}
        aria-pressed={visible}
        disabled={inputProps.disabled}
        onClick={() => setVisible((current) => !current)}
      >
        {visible ? "Ẩn" : "Hiện"}
      </button>
    </div>
  );
}
