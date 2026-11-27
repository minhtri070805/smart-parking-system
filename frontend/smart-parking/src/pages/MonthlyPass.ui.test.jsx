import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import MonthlyPass from "./MonthlyPass.jsx";
import { clearAuthSession, setAuthSession } from "../services/authSession.js";

const lots = [{ id: 1, name: "Bãi Trung Tâm" }, { id: 2, name: "Bãi Phụ" }];
const plans = [
  { id: 10, name: "Gói 1 tháng", lotId: 1, termMonths: 1, priceAmount: 800000, currency: "VND" },
  { id: 11, name: "Gói 3 tháng", lotId: 1, termMonths: 3, priceAmount: 2200000, currency: "VND" },
  { id: 20, name: "Gói bãi phụ", lotId: 2, termMonths: 1, priceAmount: 500000, currency: "VND" },
];
const vehicles = [
  { id: 5, plateNumber: "51A-123.45", isActive: true },
  { id: 6, plateNumber: "30H-999.99", isActive: true },
];
const inDays = (days) => new Date(Date.now() + days * 86_400_000).toISOString();

const ok = (data) => Response.json({ data });

describe("MonthlyPass", () => {
  let fetchMock;
  let model;
  let assign;
  const originalLocation = window.location;

  const handler = async (input, options = {}) => {
    const url = new URL(input, "http://localhost");
    if (url.pathname === "/api/v1/parking-lots") return ok({ items: model.lots, totalPages: 1 });
    if (url.pathname === "/api/v1/subscription-plans") return ok(model.plans);
    if (url.pathname === "/api/v1/vehicles") return ok({ items: model.vehicles, totalPages: 1 });
    if (url.pathname === "/api/v1/parking-passes/my") return ok(model.passes);
    if (url.pathname === "/api/payments/payos/subscriptions" && options.method === "POST")
      return model.checkout(options);
    return Response.json({ message: `unexpected ${url.pathname}` }, { status: 404 });
  };

  beforeEach(() => {
    model = {
      lots,
      plans,
      vehicles,
      passes: [],
      checkout: () => Response.json({ orderCode: 777, checkoutUrl: "https://pay.payos.vn/web/abc" }),
    };
    setAuthSession({
      accessToken: "user-token",
      refreshToken: "user-refresh",
      userProfile: { id: 7, username: "customer", role: "USER" },
    });
    fetchMock = vi.fn(handler);
    vi.stubGlobal("fetch", fetchMock);
    assign = vi.fn();
    Object.defineProperty(window, "location", { configurable: true, value: { ...originalLocation, assign } });
  });

  afterEach(() => {
    Object.defineProperty(window, "location", { configurable: true, value: originalLocation });
    vi.unstubAllGlobals();
    clearAuthSession();
  });

  const mount = () =>
    render(
      <MemoryRouter initialEntries={["/customer/monthly-pass"]}>
        <MonthlyPass />
      </MemoryRouter>,
    );
  const choose = async (plan = "Gói 1 tháng", vehicle = "5") => {
    fireEvent.click(await screen.findByRole("radio", { name: new RegExp(plan) }));
    fireEvent.change(screen.getByLabelText("Xe đăng ký"), { target: { value: vehicle } });
  };
  const checkoutCalls = () =>
    fetchMock.mock.calls.filter(([url, options]) => String(url).includes("payos/subscriptions") && options?.method === "POST");

  it("lists my passes with status, plate, lot and days left", async () => {
    model.passes = [
      { id: 1, vehicleId: 5, planId: 10, lotId: 1, status: "ACTIVE", startsAtUtc: inDays(-10), endsAtUtc: inDays(20), priceAmount: 800000, currency: "VND" },
      { id: 2, vehicleId: 6, planId: 11, lotId: 2, status: "SUSPENDED", startsAtUtc: inDays(-5), endsAtUtc: inDays(60), priceAmount: 2200000, currency: "VND" },
      { id: 3, vehicleId: 5, planId: 10, lotId: 1, status: "EXPIRED", startsAtUtc: inDays(-60), endsAtUtc: inDays(-30), priceAmount: 800000, currency: "VND" },
    ];
    mount();

    const mine = (await screen.findByRole("region", { name: "Vé của tôi" }));
    await waitFor(() => expect(within(mine).getByText("Đang hiệu lực")).toBeTruthy());
    expect(within(mine).getByText("Tạm khóa")).toBeTruthy();
    expect(within(mine).getByText("Đã hết hạn")).toBeTruthy();
    expect(within(mine).getAllByText("51A-123.45").length).toBe(2);
    expect(within(mine).getByText("Bãi Phụ")).toBeTruthy();
    expect(within(mine).getByText(/Còn 20 ngày/)).toBeTruthy();
    expect(within(mine).getByText(/Liên hệ quản lý bãi/)).toBeTruthy();
  });

  it("shows an empty state when there are no passes and no plans on sale", async () => {
    model.plans = [];
    mount();
    expect(await screen.findByText(/chưa có vé tháng nào/)).toBeTruthy();
    expect(await screen.findByText(/chưa có gói vé tháng nào đang được bán/)).toBeTruthy();
  });

  it("asks the customer to add a vehicle first", async () => {
    model.vehicles = [];
    mount();
    expect(await screen.findByText(/chưa có xe nào để đăng ký/)).toBeTruthy();
    expect(screen.getByRole("link", { name: "Thêm xe của bạn" }).getAttribute("href")).toBe("/customer/profile");
  });

  it("only offers plans of the selected lot and keeps pay disabled until plan and vehicle are chosen", async () => {
    mount();
    const pay = await screen.findByRole("button", { name: "Thanh toán bằng PayOS" });
    expect(pay.disabled).toBe(true);
    expect(screen.queryByRole("radio", { name: /Gói bãi phụ/ })).toBeNull();

    fireEvent.change(screen.getByLabelText("Bãi đỗ"), { target: { value: "2" } });
    expect(await screen.findByRole("radio", { name: /Gói bãi phụ/ })).toBeTruthy();
    expect(screen.queryByRole("radio", { name: /Gói 1 tháng/ })).toBeNull();

    fireEvent.click(screen.getByRole("radio", { name: /Gói bãi phụ/ }));
    expect(pay.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText("Xe đăng ký"), { target: { value: "6" } });
    expect(pay.disabled).toBe(false);
  });

  it("sends only plan and vehicle with an idempotency key, then redirects to the https checkout link", async () => {
    mount();
    await choose("Gói 3 tháng", "6");
    expect(screen.getByText(/30H-999.99 · Gói 3 tháng/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Thanh toán bằng PayOS" }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith("https://pay.payos.vn/web/abc"));
    const [[, options]] = checkoutCalls();
    expect(JSON.parse(options.body)).toEqual({ planId: 11, vehicleId: 6 });
    expect(options.headers["Idempotency-Key"]).toMatch(/\S{8,}/);
    expect(options.headers.Authorization).toBe("Bearer user-token");
  });

  it("never redirects to a non-https or missing checkout link", async () => {
    model.checkout = () => Response.json({ orderCode: 1, checkoutUrl: "http://evil.example/pay" });
    mount();
    await choose();
    fireEvent.click(screen.getByRole("button", { name: "Thanh toán bằng PayOS" }));

    expect((await screen.findByRole("alert")).textContent).toMatch(/link thanh toán hợp lệ/);
    expect(assign).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Thanh toán bằng PayOS" }).disabled).toBe(false);
  });

  it("explains server error codes, retries with the same key and issues a new key when the choice changes", async () => {
    model.checkout = () => Response.json({ code: "ACTIVE_PASS_CONFLICT" }, { status: 409 });
    mount();
    await choose();
    const pay = screen.getByRole("button", { name: "Thanh toán bằng PayOS" });

    fireEvent.click(pay);
    expect((await screen.findByRole("alert")).textContent).toMatch(/đã có vé tháng còn hiệu lực/);
    fireEvent.click(pay);
    await waitFor(() => expect(checkoutCalls().length).toBe(2));
    const [first, second] = checkoutCalls().map(([, o]) => o.headers["Idempotency-Key"]);
    expect(second).toBe(first);

    fireEvent.change(screen.getByLabelText("Xe đăng ký"), { target: { value: "6" } });
    fireEvent.click(pay);
    await waitFor(() => expect(checkoutCalls().length).toBe(3));
    expect(checkoutCalls()[2][1].headers["Idempotency-Key"]).not.toBe(first);
    expect(assign).not.toHaveBeenCalled();
  });

  it("blocks double submit while the checkout request is in flight", async () => {
    let release;
    model.checkout = () => new Promise((resolve) => { release = () => resolve(Response.json({ orderCode: 1, checkoutUrl: "https://pay.payos.vn/x" })); });
    mount();
    await choose();
    const pay = screen.getByRole("button", { name: "Thanh toán bằng PayOS" });
    fireEvent.click(pay);
    await screen.findByRole("button", { name: /Đang chuyển tới cổng thanh toán/ });
    fireEvent.click(screen.getByRole("button", { name: /Đang chuyển tới cổng thanh toán/ }));
    release();
    await waitFor(() => expect(assign).toHaveBeenCalledTimes(1));
    expect(checkoutCalls().length).toBe(1);
  });

  it("warns when the chosen vehicle already holds a pass at the lot", async () => {
    model.passes = [{ id: 1, vehicleId: 5, planId: 10, lotId: 1, status: "ACTIVE", startsAtUtc: inDays(-1), endsAtUtc: inDays(29), priceAmount: 1, currency: "VND" }];
    mount();
    await choose();
    expect(await screen.findByText(/Xe này đang có vé đang hiệu lực tại bãi này/)).toBeTruthy();
  });
});
