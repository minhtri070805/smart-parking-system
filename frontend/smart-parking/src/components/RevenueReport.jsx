import { useState } from "react";
import { Feedback, useResource } from "./WorkspaceUI.jsx";

const money = (value) =>
  new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND" }).format(value || 0);

export default function RevenueReport({ lotId, revision = 0, title = "Báo cáo doanh thu" }) {
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const report = useResource(
    `/v1/parking-sessions/monthly-stats?year=${year}&month=${month}${lotId ? `&lotId=${lotId}` : ""}`,
    revision,
  );
  const data = report.data?.data || report.data;
  return (
    <section className="workspace-panel">
      <div className="workspace-heading">
        <div><h2>{title}</h2><p>Doanh thu ghi nhận từ các phiên đã thanh toán.</p></div>
        <div className="workspace-filters">
          <label>Tháng<select value={month} onChange={(e) => setMonth(Number(e.target.value))}>{Array.from({ length: 12 }, (_, i) => <option key={i + 1} value={i + 1}>Tháng {i + 1}</option>)}</select></label>
          <label>Năm<select value={year} onChange={(e) => setYear(Number(e.target.value))}>{[now.getFullYear() - 2, now.getFullYear() - 1, now.getFullYear()].map((y) => <option key={y}>{y}</option>)}</select></label>
        </div>
      </div>
      <Feedback loading={report.loading} error={report.error} />
      {!report.error && !report.loading && data && <>
        <article className="manager-metrics"><div><span>Tổng doanh thu tháng</span><strong>{money(data.totalRevenue)}</strong></div><div><span>Lượt xe vào</span><strong>{data.totalCheckIns ?? 0}</strong></div><div><span>Lượt xe ra</span><strong>{data.totalCheckOuts ?? 0}</strong></div></article>
        <div className="workspace-table"><table><thead><tr><th>Ngày</th><th>Xe vào</th><th>Xe ra</th><th>Doanh thu</th></tr></thead><tbody>{(data.days || []).map((day) => <tr key={day.date}><td>{new Date(`${day.date}T00:00:00`).toLocaleDateString("vi-VN")}</td><td>{day.checkIns}</td><td>{day.checkOuts}</td><td>{money(day.revenue)}</td></tr>)}</tbody></table></div>
      </>}
    </section>
  );
}
