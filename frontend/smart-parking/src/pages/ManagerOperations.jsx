import { Link, useSearchParams } from "react-router-dom";
import ShiftManagement from "./ShiftManagement.jsx";
import CashReconciliation from "./CashReconciliation.jsx";
import "../operations.css";

const sections = [
  ["shifts", "Quản lý ca"],
  ["reconciliation", "Đối soát ca"],
];

export default function ManagerOperations() {
  const [searchParams] = useSearchParams();
  const selected = sections.some(([key]) => key === searchParams.get("view"))
    ? searchParams.get("view")
    : "shifts";

  return <>
    <nav className="workspace container manager-operations-tabs" aria-label="Chức năng quản lý ca">
      {sections.map(([key, label]) => <Link
        key={key}
        to={`/manager/shifts?view=${key}`}
        className={`btn ${selected === key ? "btn--primary" : "btn--outline"}`}
        aria-current={selected === key ? "page" : undefined}
      >{label}</Link>)}
    </nav>
    {selected === "reconciliation" ? <CashReconciliation /> : <ShiftManagement />}
  </>;
}
