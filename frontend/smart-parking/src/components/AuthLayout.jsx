import { NavLink } from "react-router-dom";

export default function AuthLayout({ title, description, children }) {
  return (
    <section className="auth-page">
      <div className="auth-shell">
        <aside className="auth-intro" aria-label="Thông tin Smart-Parking">
          <span className="auth-intro__label">Smart-Parking</span>
          <h2>Một tài khoản cho toàn bộ hành trình đỗ xe</h2>
          <p>
            Khách hàng theo dõi đặt chỗ và phiên gửi xe. Đội vận hành làm việc
            theo đúng vai trò được phân công.
          </p>
          <ul>
            <li>Trạng thái bãi và ô đỗ theo dữ liệu hệ thống</li>
            <li>Xác minh email bằng mã OTP</li>
            <li>Quyền truy cập tách biệt theo vai trò</li>
          </ul>
        </aside>
        <div className="auth-modal" aria-labelledby="auth-title">
          <nav className="auth-tabs" aria-label="Đăng nhập hoặc đăng ký">
            <NavLink to="/login" className="auth-tabs__item">
              Đăng nhập
            </NavLink>
            <NavLink to="/register" className="auth-tabs__item">
              Đăng ký
            </NavLink>
          </nav>
          <div className="auth-form-wrap">
            <h1 id="auth-title">{title}</h1>
            {description && <p className="auth-content__description">{description}</p>}
            {children}
          </div>
        </div>
      </div>
    </section>
  );
}
