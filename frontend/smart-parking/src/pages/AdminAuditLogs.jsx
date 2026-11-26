import { useState } from "react";
import RequireRole from "../components/RequireRole.jsx";
import { Feedback, Pager, dateLabel, itemsOf, query, useResource } from "../components/WorkspaceUI.jsx";
import "../workspace.css";

function values(value) {
  if (!value) return "—";
  try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; }
}

export default function AdminAuditLogs() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [action, setAction] = useState("");
  const [revision, setRevision] = useState(0);
  const resource = useResource(`/v1/admin/audit-logs?${query({ page, pageSize: 20, search, action })}`, revision);
  const data = resource.data;
  const logs = itemsOf(data);

  return <RequireRole role="ADMIN"><section className="workspace container">
    <div className="workspace-heading"><div><h1>Audit log quản trị</h1><p>Theo dõi thao tác thay đổi dữ liệu và vận hành trong hệ thống.</p></div>
      <button className="btn btn--outline" onClick={() => setRevision((value) => value + 1)}>Làm mới</button></div>
    <section className="workspace-panel">
      <div className="workspace-filters">
        <label>Tìm kiếm<input value={search} onChange={(event) => { setPage(1); setSearch(event.target.value); }} placeholder="Tài khoản, hành động, đối tượng…" /></label>
        <label>Hành động<input value={action} onChange={(event) => { setPage(1); setAction(event.target.value); }} placeholder="Ví dụ: CREATE_PARKING_LOT" /></label>
      </div>
    </section>
    <Feedback loading={resource.loading} error={resource.error} />
    {!resource.loading && !resource.error && <section className="workspace-panel audit-log-panel">
      {logs.length === 0 ? <p>Chưa có nhật ký phù hợp.</p> : <div className="workspace-table-wrap"><table className="workspace-table"><thead><tr><th>Thời gian</th><th>Người thực hiện</th><th>Hành động</th><th>Đối tượng</th><th>Thay đổi</th></tr></thead><tbody>
        {logs.map((log) => <tr key={log.id}><td>{dateLabel(log.createdAt)}</td><td>{log.actorName || log.actorUsername || "Hệ thống"}</td><td><strong>{log.action}</strong></td><td>{log.targetEntity}{log.targetId ? ` #${log.targetId}` : ""}</td><td><details><summary>Xem chi tiết</summary><pre>{`Trước:\n${values(log.oldValues)}\n\nSau:\n${values(log.newValues)}`}</pre></details></td></tr>)}
      </tbody></table></div>}
      <Pager page={page} data={data} onChange={setPage} disabled={resource.loading} />
    </section>}
  </section></RequireRole>;
}
