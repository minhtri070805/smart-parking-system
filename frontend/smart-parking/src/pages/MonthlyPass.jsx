import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import RequireCustomer from "../components/RequireCustomer.jsx";
import { Feedback } from "../components/WorkspaceUI.jsx";
import {
  dayLabel,
  daysLeft,
  formatMoney,
  monthlyPassApi,
  newIdempotencyKey,
  passStatusLabel,
  purchaseErrorMessage,
  safeCheckoutUrl,
  termLabel,
} from "../services/monthlyPass.js";
import "../workspace.css";
import "../monthly-pass.css";

function useLoad(loader) {
  const [state, setState] = useState({ loading: true, data: [], error: "" });
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setState((current) => ({ ...current, loading: true, error: "" }));
    loader(controller.signal)
      .then((data) => !controller.signal.aborted && setState({ loading: false, data, error: "" }))
      .catch((error) => {
        if (!controller.signal.aborted) setState({ loading: false, data: [], error: error.message });
      });
    return () => controller.abort();
  }, [loader, revision]);
  return { ...state, reload: () => setRevision((value) => value + 1) };
}

const loadAll = (signal) =>
  Promise.all([
    monthlyPassApi.lots(signal),
    monthlyPassApi.plans(signal),
    monthlyPassApi.vehicles(signal),
  ]).then(([lots, plans, vehicles]) => ({ lots, plans, vehicles }));

function PassList({ passes, lotName, plateOf, planName }) {
  if (!passes.length)
    return (
      <p className="mp-empty">
        Bạn chưa có vé tháng nào. Chọn gói bên dưới để đăng ký cho xe của bạn.
      </p>
    );
  return (
    <ul className="mp-passes">
      {passes.map((pass) => {
        const left = pass.status === "ACTIVE" ? daysLeft(pass.endsAtUtc) : null;
        return (
          <li key={pass.id} className={`mp-pass mp-pass--${String(pass.status).toLowerCase()}`}>
            <div className="mp-pass__head">
              <strong>{plateOf(pass.vehicleId)}</strong>
              <span className="mp-badge">{passStatusLabel(pass.status)}</span>
            </div>
            <dl>
              <div><dt>Bãi</dt><dd>{lotName(pass.lotId)}</dd></div>
              <div><dt>Gói</dt><dd>{planName(pass.planId)}</dd></div>
              <div><dt>Hiệu lực</dt><dd>{dayLabel(pass.startsAtUtc)} – {dayLabel(pass.endsAtUtc)}</dd></div>
              <div><dt>Đã trả</dt><dd>{formatMoney(pass.priceAmount, pass.currency)}</dd></div>
            </dl>
            {left != null && <p className="mp-pass__left">Còn {left} ngày</p>}
            {pass.status === "SUSPENDED" && (
              <p className="mp-pass__note">Vé đang bị tạm khóa. Liên hệ quản lý bãi để được hỗ trợ.</p>
            )}
          </li>
        );
      })}
    </ul>
  );
}

function Purchase({ lots, plans, vehicles, passes }) {
  const lotsWithPlans = useMemo(
    () => lots.filter((lot) => plans.some((plan) => plan.lotId === lot.id)),
    [lots, plans],
  );
  const [lotId, setLotId] = useState("");
  const [planId, setPlanId] = useState("");
  const [vehicleId, setVehicleId] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const keyRef = useRef({ signature: "", key: "" });

  const selectedLotId = lotId || String(lotsWithPlans[0]?.id ?? "");
  const lotPlans = plans.filter((plan) => String(plan.lotId) === selectedLotId);
  const selectedPlan = lotPlans.find((plan) => String(plan.id) === planId) || null;
  const usableVehicles = vehicles.filter((vehicle) => vehicle.isActive !== false);
  const selectedVehicle = usableVehicles.find((vehicle) => String(vehicle.id) === vehicleId) || null;
  const heldPass = selectedVehicle
    ? passes.find(
        (pass) =>
          pass.vehicleId === selectedVehicle.id &&
          String(pass.lotId) === selectedLotId &&
          ["ACTIVE", "SCHEDULED", "PENDING"].includes(pass.status),
      )
    : null;

  // Cùng gói + cùng xe dùng lại một khóa để bấm lại không tạo đơn trùng; đổi lựa chọn thì tạo khóa mới.
  const keyFor = (signature) => {
    if (keyRef.current.signature !== signature) keyRef.current = { signature, key: newIdempotencyKey() };
    return keyRef.current.key;
  };

  const pay = async (event) => {
    event.preventDefault();
    if (busy || !selectedPlan || !selectedVehicle) return;
    setBusy(true);
    setError("");
    try {
      const response = await monthlyPassApi.checkout(
        selectedPlan.id,
        selectedVehicle.id,
        keyFor(`${selectedPlan.id}:${selectedVehicle.id}`),
      );
      const order = response?.data ?? response;
      const url = safeCheckoutUrl(order?.checkoutUrl);
      if (!url) throw new Error("Không nhận được link thanh toán hợp lệ. Vui lòng thử lại.");
      window.location.assign(url);
    } catch (e) {
      setError(purchaseErrorMessage(e));
      setBusy(false);
    }
  };

  if (!lotsWithPlans.length)
    return <p className="mp-empty">Hiện chưa có gói vé tháng nào đang được bán.</p>;
  if (!usableVehicles.length)
    return (
      <p className="mp-empty">
        Bạn chưa có xe nào để đăng ký. <Link to="/customer/profile">Thêm xe của bạn</Link> rồi quay lại.
      </p>
    );

  return (
    <form className="mp-form" onSubmit={pay}>
      <label>
        Bãi đỗ
        <select
          value={selectedLotId}
          disabled={busy}
          onChange={(e) => {
            setLotId(e.target.value);
            setPlanId("");
          }}
        >
          {lotsWithPlans.map((lot) => (
            <option key={lot.id} value={lot.id}>{lot.name}</option>
          ))}
        </select>
      </label>

      <fieldset className="mp-plans" disabled={busy}>
        <legend>Chọn gói</legend>
        {lotPlans.map((plan) => (
          <label key={plan.id} className={`mp-plan ${String(plan.id) === planId ? "mp-plan--on" : ""}`}>
            <input
              type="radio"
              name="plan"
              value={plan.id}
              checked={String(plan.id) === planId}
              onChange={() => setPlanId(String(plan.id))}
            />
            <span className="mp-plan__name">{plan.name}</span>
            <span className="mp-plan__term">{termLabel(plan.termMonths)}</span>
            <strong className="mp-plan__price">{formatMoney(plan.priceAmount, plan.currency)}</strong>
          </label>
        ))}
      </fieldset>

      <label>
        Xe đăng ký
        <select value={vehicleId} disabled={busy} onChange={(e) => setVehicleId(e.target.value)}>
          <option value="">— Chọn xe —</option>
          {usableVehicles.map((vehicle) => (
            <option key={vehicle.id} value={vehicle.id}>{vehicle.plateNumber}</option>
          ))}
        </select>
      </label>

      {heldPass && (
        <p className="mp-hint">
          Xe này đang có vé {passStatusLabel(heldPass.status).toLowerCase()} tại bãi này
          {heldPass.status === "ACTIVE" ? ` (đến ${dayLabel(heldPass.endsAtUtc)})` : ""}.
        </p>
      )}
      {selectedPlan && selectedVehicle && (
        <p className="mp-summary">
          {selectedVehicle.plateNumber} · {selectedPlan.name} · <strong>{formatMoney(selectedPlan.priceAmount, selectedPlan.currency)}</strong>
        </p>
      )}
      {error && <p className="workspace-error" role="alert">{error}</p>}
      <button className="btn btn--primary" type="submit" disabled={busy || !selectedPlan || !selectedVehicle}>
        {busy ? "Đang chuyển tới cổng thanh toán…" : "Thanh toán bằng PayOS"}
      </button>
      <p className="mp-fine">
        Vé chỉ có hiệu lực sau khi PayOS xác nhận thanh toán. Số tiền và thời hạn do hệ thống tính.
      </p>
    </form>
  );
}

function MonthlyPassContent() {
  const all = useLoad(loadAll);
  const passes = useLoad(monthlyPassApi.myPasses);
  const { lots = [], plans = [], vehicles = [] } = all.data?.lots ? all.data : {};
  const lotName = useCallback((id) => lots.find((lot) => lot.id === id)?.name || `Bãi #${id}`, [lots]);
  const plateOf = useCallback(
    (id) => vehicles.find((vehicle) => vehicle.id === id)?.plateNumber || `Xe #${id}`,
    [vehicles],
  );
  const planName = useCallback((id) => plans.find((plan) => plan.id === id)?.name || `Gói #${id}`, [plans]);

  return (
    <section className="workspace container mp-page">
      <div className="workspace-heading">
        <div>
          <h1>Vé tháng</h1>
          <p>Đăng ký gửi xe theo tháng và theo dõi thời hạn vé của bạn.</p>
        </div>
        <button
          className="btn btn--outline"
          disabled={all.loading || passes.loading}
          onClick={() => {
            all.reload();
            passes.reload();
          }}
        >
          Làm mới
        </button>
      </div>

      <section className="workspace-panel" aria-labelledby="mp-mine">
        <h2 id="mp-mine">Vé của tôi</h2>
        <Feedback loading={passes.loading} error={passes.error} />
        {!passes.loading && !passes.error && (
          <PassList passes={passes.data} lotName={lotName} plateOf={plateOf} planName={planName} />
        )}
      </section>

      <section className="workspace-panel" aria-labelledby="mp-buy">
        <h2 id="mp-buy">Đăng ký vé tháng</h2>
        <Feedback loading={all.loading} error={all.error} />
        {!all.loading && !all.error && (
          <Purchase lots={lots} plans={plans} vehicles={vehicles} passes={passes.data} />
        )}
      </section>
    </section>
  );
}

export default function MonthlyPass() {
  return (
    <RequireCustomer>
      <MonthlyPassContent />
    </RequireCustomer>
  );
}
