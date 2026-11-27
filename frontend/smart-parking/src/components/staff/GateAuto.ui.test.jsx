import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import GateOperations from "./GateOperations.jsx";
import { clearAuthSession, setAuthSession } from "../../services/authSession.js";
import { isLowConfidence, newTicketCode, findBooking, singleZone } from "../../services/gateAuto.js";

const lot = { lotId: 1, lotName: "Bãi Trung Tâm", isActive: true };
const ok = (data) => Response.json({ data });
const inDays = (days) => new Date(Date.now() + days * 86_400_000).toISOString();
const fixedZone = { id: 11, code: "G", name: "Tầng G vé tháng", type: "FIXED", isActive: true };
const transientZone = { id: 12, code: "A", name: "Khu vãng lai", type: "TRANSIENT", isActive: true };
const validPass = {
  canEnter: true, reason: "OK", message: "Vé tháng còn hiệu lực.", plateNumber: "51F-123.45",
  passId: 1, vehicleId: 5, planName: "Gói 1 tháng", status: "ACTIVE", startsAtUtc: inDays(-5), endsAtUtc: inDays(25),
};
const noPass = { ...validPass, canEnter: false, reason: "PASS_NOT_FOUND", message: "Không tìm thấy vé tháng của xe này tại bãi." };
const booking = { id: 31, plateNumber: "30H-999.99", paymentMode: "PREPAY", paymentStatus: "PAID", status: "PENDING" };

let model;
let fetchMock;
const calls = (method, suffix) =>
  fetchMock.mock.calls.filter(([url, options]) => (options?.method || "GET") === method && String(url).split("?")[0].endsWith(suffix));
const bodyOf = (call) => JSON.parse(call[1].body);

beforeEach(() => {
  model = {
    zones: [fixedZone, transientZone],
    pass: () => ok(noPass),
    bookings: () => ok({ items: [], totalCount: 0, totalPages: 0, pageNumber: 1, pageSize: 100 }),
    entry: (body) => ok({ id: 90, ticketCode: body.ticketCode, temporaryPlateNumber: body.plateNumber }),
    passEntry: (body) => ok({ session: { id: 77, plateNumber: body.plateNumber }, pass: validPass, replay: false }),
    bookingEntry: () => ok({ id: 120 }),
    detect: null,
  };
  localStorage.clear();
  setAuthSession({ accessToken: "staff-token", refreshToken: "staff-refresh", userProfile: { id: 2, username: "staff", role: "STAFF" } });
  fetchMock = vi.fn(async (input, options = {}) => {
    const url = new URL(input, "http://localhost");
    const path = url.pathname;
    const method = options.method || "GET";
    if (path.endsWith("/operations/zones")) return ok(model.zones);
    if (path.endsWith("/operations/passes/lookup")) return model.pass(url.searchParams.get("plate"));
    if (path.endsWith("/operations/pass-entries") && method === "POST") return model.passEntry(JSON.parse(options.body));
    if (path.endsWith("/operations/entries") && method === "POST") return model.entry(JSON.parse(options.body));
    if (path.includes("/parking-sessions/by-plate/")) return model.byPlate ? model.byPlate() : Response.json({ message: "not found" }, { status: 404 });
    if (path === "/api/v1/parking-sessions/active") return ok({ items: [], totalCount: 0, totalPages: 0 });
    if (path === "/api/v1/bookings") return model.bookings();
    if (/\/bookings\/\d+\/check-in-at-gate$/.test(path) && method === "POST") return model.bookingEntry(JSON.parse(options.body));
    if (path === "/api/v1/images") return ok({ id: 88, url: "https://images.example.invalid/gate.jpg" });
    if (path.endsWith("/recognition/detect")) return Response.json(model.detect);
    if (path === "/api/v1/parking-sessions/10" && method === "GET")
      return ok({ id: 10, lotId: 1, lotName: "Bãi Trung Tâm", plateNumber: "51F-123.45", status: "PAID", verificationStatus: "VERIFIED", checkInTime: "2026-11-27T01:00:00Z", totalFee: 15000, ticketCode: "VE-0042" });
    return Response.json({ message: `unexpected ${method} ${path}` }, { status: 404 });
  });
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
  localStorage.clear();
  clearAuthSession();
});

const photo = () => new File(["photo"], "gate.jpg", { type: "image/jpeg" });
const mountGate = (props = {}) => {
  const handlers = { onChanged: vi.fn(), onOpen: vi.fn() };
  render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <GateOperations lot={lot} revision={0} onBusyChange={() => {}} autoDelaySeconds={0} {...handlers} {...props} />
    </MemoryRouter>,
  );
  return handlers;
};
const typePlate = async (user, plate) => {
  await user.type(await screen.findByLabelText("Biển số xe"), plate);
  await user.click(screen.getByRole("button", { name: "Kiểm tra" }));
};
const scanPhoto = async (user, recognition) => {
  model.detect = recognition;
  await user.upload(await screen.findByLabelText(/Chọn \/ chụp ảnh/), photo());
};

describe("Gate automation helpers", () => {
  it("only trusts the AI when it is confident enough", () => {
    expect(isLowConfidence(null)).toBe(false);
    expect(isLowConfidence({ confidence: 0.95 })).toBe(false);
    expect(isLowConfidence({ confidence: 0.8 })).toBe(false);
    expect(isLowConfidence({ confidence: 0.79 })).toBe(true);
    expect(isLowConfidence({})).toBe(false);
  });

  it("issues ticket codes that Core accepts and that differ from one call to the next", () => {
    const a = newTicketCode(1_800_000_000_000, () => 0.123);
    const b = newTicketCode(1_800_000_000_000, () => 0.987);
    expect(a).toMatch(/^[A-Z0-9-]{3,40}$/);
    expect(b).toMatch(/^VL-[A-Z0-9]+$/);
    expect(a).not.toBe(b);
  });

  it("picks a zone automatically only when there is exactly one candidate", () => {
    const zones = [fixedZone, transientZone, { ...transientZone, id: 13, code: "B" }, { ...fixedZone, id: 14, isActive: false }];
    expect(singleZone(zones, "FIXED").id).toBe(11);
    expect(singleZone(zones, "TRANSIENT")).toBeNull();
    expect(singleZone([], "FIXED")).toBeNull();
    expect(singleZone(null, "FIXED")).toBeNull();
  });

  it("finds a waiting booking for the plate and refuses unpaid prepaid ones", () => {
    expect(findBooking([booking], "30H99999").booking.id).toBe(31);
    expect(findBooking([booking], "51F12345").booking).toBeNull();
    const unpaid = { ...booking, paymentStatus: "PENDING" };
    expect(findBooking([unpaid], "30H-999.99")).toEqual({ booking: null, unpaid });
    const atCounter = { ...booking, paymentMode: "PAY_AT_COUNTER", paymentStatus: "PAY_AT_COUNTER" };
    expect(findBooking([atCounter], "30H-999.99").booking.id).toBe(31);
  });
});

describe("Gate automation: entries are let in without a click", () => {
  it("lets a pass holder in through the only fixed zone and lists it under 'Vừa cho vào'", async () => {
    model.pass = () => ok(validPass);
    const user = userEvent.setup();
    const { onChanged } = mountGate();
    await typePlate(user, "51F12345");

    expect(await screen.findByText(/Đã tự động cho xe/)).toBeTruthy();
    expect(screen.getByText(/bằng vé tháng \(lượt #77\)/)).toBeTruthy();
    const body = bodyOf(calls("POST", "/pass-entries")[0]);
    expect(body).toMatchObject({ zoneId: 11, plateNumber: "51F-123.45", imageId: null });
    expect(body.eventId).toMatch(/[0-9a-f-]{36}/);
    expect(onChanged).toHaveBeenCalledTimes(1);
    expect(calls("POST", "/entries")).toHaveLength(0);

    const list = screen.getByRole("region", { name: "Xe vừa được tự động cho vào" });
    expect(list.textContent).toMatch(/51F-123\.45/);
    expect(list.textContent).toMatch(/Vé tháng · Gói 1 tháng · lượt #77/);
    // Màn sẵn sàng cho xe kế tiếp.
    expect(screen.getByLabelText("Biển số xe").value).toBe("");
  });

  it("issues an electronic ticket and keeps the AI plate for a walk-in, then links to slot verification", async () => {
    const user = userEvent.setup();
    const { onOpen } = mountGate();
    await typePlate(user, "30H99999");

    expect(await screen.findByText(/như xe vãng lai \(lượt #90\)/)).toBeTruthy();
    const body = bodyOf(calls("POST", "/entries")[0]);
    expect(body).toMatchObject({ zoneId: 12, plateNumber: "30H-999.99" });
    expect(body.ticketCode).toMatch(/^VL-[A-Z0-9]+$/);
    expect(body.manualReason).toContain("30H-999.99");
    expect(calls("POST", "/pass-entries")).toHaveLength(0);

    await user.click(screen.getByRole("button", { name: "Đối chiếu tại ô" }));
    expect(onOpen).toHaveBeenCalledWith("verify", 90);
  });

  it("confirms a waiting booking by plate, sending the saved plate photo", async () => {
    model.bookings = () => ok({ items: [booking], totalCount: 1, totalPages: 1, pageNumber: 1, pageSize: 100 });
    const user = userEvent.setup();
    const { onOpen } = mountGate();
    await scanPhoto(user, { id: 501, plateNumber: "30H-999.99", confidence: 0.93 });

    expect(await screen.findByText(/theo đặt chỗ \(lượt #120\)/)).toBeTruthy();
    expect(calls("POST", "/entries")).toHaveLength(0);
    const call = fetchMock.mock.calls.find(([url]) => String(url).endsWith("/bookings/31/check-in-at-gate"));
    expect(JSON.parse(call[1].body)).toEqual({ plateNumber: "30H-999.99", checkInImageUrl: "https://images.example.invalid/gate.jpg" });

    await user.click(screen.getByRole("button", { name: "Phân ô" }));
    expect(onOpen).toHaveBeenCalledWith("booking-dispatch", undefined);
  });

  it("starts by itself as soon as the AI has read the plate, without pressing Kiểm tra", async () => {
    model.pass = () => ok(validPass);
    const user = userEvent.setup();
    mountGate();
    await scanPhoto(user, { id: 502, plateNumber: "51F-123.45", confidence: 0.97 });

    await screen.findByText(/bằng vé tháng \(lượt #77\)/);
    expect(calls("POST", "/pass-entries")).toHaveLength(1);
    expect(bodyOf(calls("POST", "/pass-entries")[0])).toMatchObject({ imageId: 88 });
  });
});

describe("Gate automation: the system stops and asks staff instead of guessing", () => {
  it("does not let a car in when the AI is not confident, and offers the manual form", async () => {
    const user = userEvent.setup();
    mountGate();
    await scanPhoto(user, { id: 503, plateNumber: "30H-999.99", confidence: 0.5 });

    expect(await screen.findByText(/AI chỉ chắc 50% về biển số/)).toBeTruthy();
    expect(await screen.findByLabelText("Mã vé")).toBeTruthy();
    expect(calls("POST", "/entries")).toHaveLength(0);
  });

  it("needs a plate photo to confirm a booking and points staff to the booking screen", async () => {
    model.bookings = () => ok({ items: [booking], totalCount: 1, totalPages: 1, pageNumber: 1, pageSize: 100 });
    const user = userEvent.setup();
    const { onOpen } = mountGate();
    await typePlate(user, "30H99999");

    expect(await screen.findByText(/Cần ảnh biển số để xác nhận xe đặt chỗ/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Mở màn Xe đặt qua web" }));
    expect(onOpen).toHaveBeenCalledWith("bookings");
    expect(calls("POST", "/entries")).toHaveLength(0);
  });

  it("never treats an unpaid prepaid booking as a walk-in", async () => {
    model.bookings = () => ok({ items: [{ ...booking, paymentStatus: "PENDING" }], totalCount: 1, totalPages: 1, pageNumber: 1, pageSize: 100 });
    const user = userEvent.setup();
    mountGate();
    await typePlate(user, "30H99999");

    expect(await screen.findByText(/chưa thanh toán tiền cọc/)).toBeTruthy();
    expect(calls("POST", "/entries")).toHaveLength(0);
    expect(screen.queryByLabelText("Mã vé")).toBeNull();
  });

  it("stops when the booking list cannot be loaded instead of guessing walk-in", async () => {
    model.bookings = () => Response.json({ message: "down" }, { status: 500 });
    const user = userEvent.setup();
    mountGate();
    await typePlate(user, "30H99999");

    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(calls("POST", "/entries")).toHaveLength(0);
  });

  it("asks staff to choose when the lot has several walk-in zones", async () => {
    model.zones = [fixedZone, transientZone, { ...transientZone, id: 13, code: "B", name: "Khu B" }];
    const user = userEvent.setup();
    mountGate();
    await typePlate(user, "30H99999");

    expect(await screen.findByText(/không có đúng một khu vãng lai/)).toBeTruthy();
    expect(await screen.findByLabelText("Mã vé")).toBeTruthy();
    expect(calls("POST", "/entries")).toHaveLength(0);
  });

  it("lets nobody in automatically while the lot is closed", async () => {
    model.pass = () => ok(validPass);
    const user = userEvent.setup();
    mountGate({ lot: { ...lot, isActive: false } });
    await typePlate(user, "51F12345");
    expect(await screen.findByText(/Bãi đang đóng nhận xe/)).toBeTruthy();
    expect(calls("POST", "/pass-entries")).toHaveLength(0);
  });

  it("never exits a car by itself: an open session goes to the exit confirmation", async () => {
    model.byPlate = () => ok({ id: 10, lotId: 1, lotName: "Bãi Trung Tâm", plateNumber: "51F-123.45", status: "PAID", verificationStatus: "VERIFIED", checkInTime: "2026-11-27T01:00:00Z", totalFee: 15000, ticketCode: "VE-0042" });
    const user = userEvent.setup();
    mountGate();
    await typePlate(user, "51F12345");

    expect(await screen.findByRole("button", { name: "Xác nhận xe đã ra bãi" })).toBeTruthy();
    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith("/check-out"))).toBe(false);
    expect(calls("POST", "/entries")).toHaveLength(0);
  });
});

describe("Gate automation: staff stays in control", () => {
  it("waits for the countdown, then lets staff send immediately or cancel to fix the plate", async () => {
    const user = userEvent.setup();
    mountGate({ autoDelaySeconds: 30 });
    await typePlate(user, "30H99999");

    expect(await screen.findByText(/Tự động cho vào sau/)).toBeTruthy();
    expect(calls("POST", "/entries")).toHaveLength(0);

    await user.click(screen.getByRole("button", { name: "Hủy, sửa biển số" }));
    expect(calls("POST", "/entries")).toHaveLength(0);
    expect(screen.getByLabelText("Biển số xe").value).toBe("30H-999.99");

    await user.clear(screen.getByLabelText("Biển số xe"));
    await user.type(screen.getByLabelText("Biển số xe"), "30H99998");
    await user.click(screen.getByRole("button", { name: "Kiểm tra" }));
    await user.click(await screen.findByRole("button", { name: "Cho vào ngay" }));
    await screen.findByText(/như xe vãng lai \(lượt #90\)/);
    expect(bodyOf(calls("POST", "/entries")[0]).plateNumber).toBe("30H-999.98");
  });

  it("changing the plate while waiting cancels the pending entry", async () => {
    const user = userEvent.setup();
    mountGate({ autoDelaySeconds: 30 });
    await typePlate(user, "30H99999");
    await screen.findByText(/Tự động cho vào sau/);
    await user.type(screen.getByLabelText("Biển số xe"), "{Backspace}");
    expect(screen.queryByText(/Tự động cho vào sau/)).toBeNull();
    expect(calls("POST", "/entries")).toHaveLength(0);
  });

  it("retries an unknown failure with the same event id and ticket so it cannot duplicate the visit", async () => {
    let attempts = 0;
    model.entry = (body) => {
      attempts += 1;
      return attempts === 1 ? new Response("", { status: 503 }) : ok({ id: 90, ticketCode: body.ticketCode });
    };
    const user = userEvent.setup();
    mountGate();
    await typePlate(user, "30H99999");

    await user.click(await screen.findByRole("button", { name: "Thử lại" }));
    await screen.findByText(/như xe vãng lai \(lượt #90\)/);
    const [first, second] = calls("POST", "/entries").map(bodyOf);
    expect(second.eventId).toBe(first.eventId);
    expect(second.ticketCode).toBe(first.ticketCode);
  });

  it("falls back to the manual form when Core clearly refuses, keeping the reason", async () => {
    model.entry = () => Response.json({ code: "CAPACITY_EXHAUSTED", message: "Khu đã hết khả năng nhận xe." }, { status: 409 });
    const user = userEvent.setup();
    mountGate();
    await typePlate(user, "30H99999");

    await user.click(await screen.findByRole("button", { name: "Xử lý thủ công" }));
    expect(await screen.findByText(/Khu đã hết khả năng nhận xe/)).toBeTruthy();
    expect(await screen.findByLabelText("Mã vé")).toBeTruthy();
  });

  it("issues a fresh ticket once when the electronic ticket code is already used", async () => {
    let attempts = 0;
    model.entry = (body) => {
      attempts += 1;
      return attempts === 1
        ? Response.json({ code: "TICKET_USED", message: "Mã vé đã có lịch sử." }, { status: 409 })
        : ok({ id: 91, ticketCode: body.ticketCode });
    };
    const user = userEvent.setup();
    mountGate();
    await typePlate(user, "30H99999");

    await screen.findByText(/như xe vãng lai \(lượt #91\)/);
    const [first, second] = calls("POST", "/entries").map(bodyOf);
    expect(second.ticketCode).not.toBe(first.ticketCode);
    expect(second.eventId).not.toBe(first.eventId);
  });

  it("remembers the automatic switch and, when it is off, shows the manual form instead", async () => {
    const user = userEvent.setup();
    mountGate({ autoAdmit: undefined });
    const toggle = await screen.findByRole("checkbox", { name: /Tự động cho xe vào/ });
    expect(toggle.checked).toBe(true);
    await user.click(toggle);
    expect(localStorage.getItem("smart-parking.gate.auto")).toBe("off");

    await typePlate(user, "30H99999");
    expect(await screen.findByLabelText("Mã vé")).toBeTruthy();
    expect(calls("POST", "/entries")).toHaveLength(0);
  });
});
