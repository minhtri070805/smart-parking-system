import { useEffect, useRef } from "react";
import { PARKING_POLICIES } from "../data/policyConstants.js";

export default function PolicyModal({ isOpen, onClose, onAccept }) {
  const closeButtonRef = useRef(null);

  useEffect(() => {
    if (!isOpen) return undefined;

    const previouslyFocused = document.activeElement;
    const handleKeyDown = (event) => {
      if (event.key === "Escape") onClose();
    };

    document.addEventListener("keydown", handleKeyDown);
    closeButtonRef.current?.focus();
    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      previouslyFocused?.focus?.();
    };
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const acceptPolicies = () => {
    onAccept();
    onClose();
  };

  return (
    <div
      className="policy-modal__backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <section
        className="policy-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="policy-modal-title"
      >
        <header className="policy-modal__header">
          <h2 id="policy-modal-title">Điều khoản sử dụng &amp; Chính sách bảo mật</h2>
          <button
            ref={closeButtonRef}
            className="policy-modal__close"
            type="button"
            aria-label="Đóng điều khoản và chính sách"
            onClick={onClose}
          >
            ×
          </button>
        </header>
        <div className="policy-modal__content">
          <ol className="policy-modal__list">
            {PARKING_POLICIES.map((policy) => (
              <li key={policy.title}>
                <h3>{policy.title}</h3>
                <p>{policy.content}</p>
              </li>
            ))}
          </ol>
        </div>
        <footer className="policy-modal__footer">
          <button className="auth-submit" type="button" onClick={acceptPolicies}>
            Tôi đã hiểu và Đồng ý
          </button>
        </footer>
      </section>
    </div>
  );
}
