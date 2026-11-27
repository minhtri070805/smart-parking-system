import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import PaymentResult from "./PaymentResult.jsx";
import { clearAuthSession, setAuthSession } from "../services/authSession.js";

const order = (status) => ({
  orderCode: 555,
  planId: 10,
  vehicleId: 5,
  amount: 800000,
  currency: "VND",
  billingPeriodStartUtc: "2026-12-01T00:00:00Z",
  billingPeriodEndUtc: "2026-12-31T00:00:00Z",
  status,
});

describe("PaymentResult", () => {
  let fetchMock;
  let statuses;

  beforeEach(() => {
    vi.useFakeTimers();
    statuses = [];
    setAuthSession({
      accessToken: "user-token",
      refreshToken: "user-refresh",
      userProfile: { id: 7, username: "customer", role: "USER" },
    });
    fetchMock = vi.fn(async (input) => {
      const url = new URL(input, "http://localhost");
      if (url.pathname === "/api/payments/payos/subscriptions/555") {
        const next = statuses.length > 1 ? statuses.shift() : statuses[0];
        if (next === 404) return Response.json({ code: "PAYMENT_NOT_FOUND" }, { status: 404 });
        return Response.json(order(next));
      }
      return Response.json({}, { status: 404 });
    });
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    clearAuthSession();
  });

  const mount = (search) =>
    render(
      <MemoryRouter initialEntries={[`/payment/result${search}`]}>
        <PaymentResult />
      </MemoryRouter>,
    );
  const settle = async (ms = 0) => act(async () => { await vi.advanceTimersByTimeAsync(ms); });

  it("keeps polling until Payment Service confirms, then reports the pass as activated", async () => {
    statuses = ["PENDING", "PAID_PENDING_CORE_CONFIRMATION", "CONFIRMED"];
    mount("?orderCode=555");
    await settle();
    expect(screen.getByText("Chờ bạn thanh toán")).toBeTruthy();
    await settle(4000);
    expect(screen.getByText(/đang kích hoạt vé/)).toBeTruthy();
    await settle(4000);
    expect(screen.getByText(/vé đã được kích hoạt/)).toBeTruthy();
    expect(screen.getByText(/800\.000/)).toBeTruthy();

    const calls = fetchMock.mock.calls.length;
    await settle(20000);
    expect(fetchMock.mock.calls.length).toBe(calls);
    expect(screen.getByRole("link", { name: "Về trang Vé tháng" }).getAttribute("href")).toBe("/customer/monthly-pass");
  });

  it("does not trust status or cancel values in the return URL", async () => {
    statuses = ["FAILED"];
    mount("?orderCode=555&status=PAID&code=00&cancel=false");
    await settle();
    expect(screen.getByText("Thanh toán không thành công")).toBeTruthy();
    expect(screen.queryByText(/vé đã được kích hoạt/)).toBeNull();
  });

  it("stops after the polling window and tells the customer not to pay again", async () => {
    statuses = ["PAID_PENDING_CORE_CONFIRMATION"];
    mount("?orderCode=555");
    await settle(4000 * 31);
    expect(screen.getByText(/Bạn không cần thanh toán lại/)).toBeTruthy();
    const calls = fetchMock.mock.calls.length;
    await settle(20000);
    expect(fetchMock.mock.calls.length).toBe(calls);
  });

  it("rejects a missing or malformed order code without calling the API", async () => {
    mount("?orderCode=abc");
    await settle();
    expect(screen.getByRole("alert").textContent).toMatch(/Thiếu mã đơn/);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("reports an order that does not belong to the account", async () => {
    statuses = [404];
    mount("?orderCode=555");
    await settle();
    expect(screen.getByRole("alert").textContent).toMatch(/Không tìm thấy đơn thanh toán/);
  });
});
