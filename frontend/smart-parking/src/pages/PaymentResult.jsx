import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import RequireCustomer from "../components/RequireCustomer.jsx";
import {
  dayLabel,
  formatMoney,
  isOrderFinal,
  monthlyPassApi,
  orderState,
} from "../services/monthlyPass.js";
import "../workspace.css";
import "../monthly-pass.css";

const POLL_MS = 4000;
const MAX_POLLS = 30; // ~2 phút; webhook PayOS thường tới trong vài giây.

function ResultContent() {
  const [params] = useSearchParams();
  const orderCode = /^\d{1,18}$/.test(params.get("orderCode") || "") ? params.get("orderCode") : "";
  const [state, setState] = useState({ order: null, error: "", timedOut: false });

  useEffect(() => {
    if (!orderCode) return undefined;
    const controller = new AbortController();
    let timer;
    let polls = 0;
    // Chỉ tin trạng thái do Payment Service xác nhận; tham số status/cancel trên URL có thể bị sửa.
    const poll = async () => {
      try {
        const order = await monthlyPassApi.order(orderCode, controller.signal);
        if (controller.signal.aborted) return;
        setState({ order, error: "", timedOut: false });
        if (isOrderFinal(order?.status)) return;
      } catch (error) {
        if (controller.signal.aborted) return;
        if (error.status === 404) {
          setState({ order: null, error: "Không tìm thấy đơn thanh toán này trong tài khoản của bạn.", timedOut: false });
          return;
        }
        setState((current) => ({ ...current, error: error.message }));
      }
      polls += 1;
      if (polls >= MAX_POLLS) {
        setState((current) => ({ ...current, timedOut: true }));
        return;
      }
      timer = setTimeout(poll, POLL_MS);
    };
    poll();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [orderCode]);

  const { order, error, timedOut } = state;
  const info = orderState(order?.status);
  const waiting = order && !isOrderFinal(order.status) && !timedOut;

  return (
    <section className="workspace container mp-page">
      <div className="workspace-heading">
        <div>
          <h1>Kết quả thanh toán vé tháng</h1>
          {orderCode && <p>Mã đơn: {orderCode}</p>}
        </div>
      </div>
      <section className="workspace-panel mp-result" aria-live="polite">
        {!orderCode && <p className="workspace-error" role="alert">Thiếu mã đơn thanh toán.</p>}
        {orderCode && !order && !error && <p role="status">Đang kiểm tra thanh toán…</p>}
        {error && <p className="workspace-error" role="alert">{error}</p>}
        {order && (
          <>
            <p className={`mp-result__state mp-result__state--${info.tone}`} role="status">
              {waiting && <span className="mp-spinner" aria-hidden="true" />}
              {info.label}
            </p>
            <dl className="mp-result__facts">
              <div><dt>Số tiền</dt><dd>{formatMoney(order.amount, order.currency)}</dd></div>
              <div>
                <dt>Kỳ vé</dt>
                <dd>{dayLabel(order.billingPeriodStartUtc)} – {dayLabel(order.billingPeriodEndUtc)}</dd>
              </div>
            </dl>
            {timedOut && !isOrderFinal(order.status) && (
              <p className="mp-hint">
                Hệ thống vẫn đang xử lý. Bạn không cần thanh toán lại — kiểm tra vé ở trang Vé tháng sau ít phút.
              </p>
            )}
          </>
        )}
        <Link className="btn btn--primary" to="/customer/monthly-pass">Về trang Vé tháng</Link>
      </section>
    </section>
  );
}

export default function PaymentResult() {
  return (
    <RequireCustomer>
      <ResultContent />
    </RequireCustomer>
  );
}
