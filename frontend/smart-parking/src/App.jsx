import { Link, Navigate, Routes, Route, useLocation } from "react-router-dom";
import { lazy, Suspense, useEffect, useSyncExternalStore } from "react";
import RequireRole from "./components/RequireRole.jsx";
import { getAuthSession, subscribeAuthSession } from "./services/authSession.js";
import "./app-shell.css";
import Header from "./components/Header.jsx";
import Footer from "./components/Footer.jsx";

const Home = lazy(() => import("./pages/Home.jsx"));
const Login = lazy(() => import("./pages/Login.jsx"));
const Register = lazy(() => import("./pages/Register.jsx"));
const CustomerDashboard = lazy(() => import("./pages/CustomerDashboard.jsx"));
const CustomerProfile = lazy(() => import("./pages/CustomerProfile.jsx"));
const CustomerSessions = lazy(() => import("./pages/CustomerSessions.jsx"));
const MyTickets = lazy(() => import("./pages/MyTickets.jsx"));
const ManagerWorkspace = lazy(() => import("./pages/ManagerWorkspace.jsx"));
const ManagerOperations = lazy(() => import("./pages/ManagerOperations.jsx"));
const RevenuePage = lazy(() => import("./pages/RevenuePage.jsx"));
const AdminDashboard = lazy(() => import("./pages/AdminDashboard.jsx"));
const AdminParkingSettings = lazy(() => import("./pages/AdminParkingSettings.jsx"));
const AdminPeople = lazy(() => import("./pages/AdminPeople.jsx"));
const AdminAuditLogs = lazy(() => import("./pages/AdminAuditLogs.jsx"));
const StaffWorkspace = lazy(() => import("./pages/StaffWorkspace.jsx"));
const ParkingScene = lazy(() => import("./pages/ParkingScene.jsx"));
const ParkingSlotManagement = lazy(() => import("./pages/ParkingSlotManagement.jsx"));
const ProjectInformation = lazy(() => import("./pages/ProjectInformation.jsx"));
const BookingWorkflowDemo = lazy(() => import("./pages/BookingWorkflowDemo.jsx"));

function RouteLoader() {
  return (
    <div className="route-loader" role="status" aria-live="polite">
      <span className="route-loader__spinner" aria-hidden="true" />
      <span>Đang mở trang…</span>
    </div>
  );
}

export default function App() {
  const { pathname, hash } = useLocation();
  const session = useSyncExternalStore(subscribeAuthSession, getAuthSession, getAuthSession);
  const workspace = ["/admin", "/manager", "/staff", "/customer", "/parking", "/booking-demo"].some(p => pathname.startsWith(p));
  const authPage = pathname === "/login" || pathname === "/register";
  useEffect(() => {
    if (hash)
      document
        .getElementById(hash.slice(1))
        ?.scrollIntoView({ block: "start" });
    else window.scrollTo?.(0, 0);
  }, [pathname, hash]);

  return (
    <div className={`app-frame ${workspace ? "app-frame--workspace" : authPage ? "app-frame--auth" : ""}`}>
      <Header workspace={workspace} />
      <main id="main-content">
        <Suspense fallback={<RouteLoader />}>
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route path="/thong-tin/:topic" element={<ProjectInformation />} />
          <Route path="/booking-demo" element={<BookingWorkflowDemo />} />
          <Route path="/customer" element={<CustomerDashboard />} />
          <Route path="/parking" element={<ParkingScene />} />
          <Route path="/parking/3d" element={<ParkingScene />} />
          <Route path="/parking/slots" element={<RequireRole role={["ADMIN", "MANAGER"]}><ParkingSlotManagement role={session?.userProfile.role} /></RequireRole>} />
          <Route path="/customer/sessions" element={<CustomerSessions />} />
          <Route path="/customer/my-tickets" element={<MyTickets />} />
          <Route path="/customer/profile" element={<CustomerProfile />} />
          <Route path="/admin" element={<AdminDashboard />} />
          <Route path="/admin/parking-settings" element={<AdminParkingSettings />} />
          <Route path="/admin/people" element={<AdminPeople />} />
          <Route path="/admin/audit-logs" element={<AdminAuditLogs />} />
          <Route path="/manager" element={<ManagerWorkspace />} />
          <Route path="/manager/revenue" element={<RequireRole role="MANAGER"><RevenuePage /></RequireRole>} />
          <Route path="/manager/shifts" element={<RequireRole role="MANAGER"><ManagerOperations /></RequireRole>} />
          <Route path="/revenue" element={<RequireRole role="MANAGER"><RevenuePage /></RequireRole>} />
          <Route path="/manager/reconciliation" element={<RequireRole role="MANAGER"><Navigate to="/manager/shifts?view=reconciliation" replace /></RequireRole>} />
          <Route path="/staff" element={<StaffWorkspace />} />
          <Route
            path="*"
            element={
              <section className="workspace container">
                <div className="workspace-panel workspace-empty">
                  <h1>Không tìm thấy trang</h1>
                  <p>Đường dẫn này không còn khả dụng.</p>
                  <Link className="btn btn--primary" to="/">
                    Về trang chủ
                  </Link>
                </div>
              </section>
            }
          />
        </Routes>
        </Suspense>
      </main>
      {!workspace && !authPage && (
          <Footer />
        )}
    </div>
  );
}
