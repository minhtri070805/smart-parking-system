import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import GateOperations from "./GateOperations.jsx";
import { StaffExit } from "./StaffExit.jsx";
import { TicketVerification } from "./TicketOperations.jsx";
import { clearAuthSession, setAuthSession } from "../../services/authSession.js";
import { staffNavigationFor } from "../../services/staffDuty.js";

const lot = { lotId: 1, lotName: "Bãi Trung Tâm", isActive: true };
const ok = (data) => Response.json({ data });
const inDays = (days) => new Date(Date.now() + days * 86_400_000).toISOString();
const zones = [
  { id: 11, code: "G", name: "Tầng G vé tháng", type: "FIXED", isActive: true },
  { id: 12, code: "A", name: "Khu vãng lai", type: "TRANSIENT", isActive: true },
];
const validPass = {
  canEnter: true, reason: "OK", message: "Vé tháng còn hiệu lực.", plateNumber: "51F-123.45",
  passId: 1, vehicleId: 5, planName: "Gói 1 tháng", status: "ACTIVE", startsAtUtc: inDays(-5), endsAtUtc: inDays(25),
};

let model;
let fetchMock;
const calls = (method, suffix) =>
  fetchMock.mock.calls.filter(([url, options]) => (options?.method || "GET") === method && String(url).split("?")[0].endsWith(suffix));

beforeEach(() => {
  model = {
    zones,
    lookup: () => ok(validPass),
    entry: (body) => ok({ session: { id: 77, plateNumber: body.plateNumber }, pass: validPass, replay: false }),
    session: null,
    byPlate: () => Response.json({ message: "not found" }, { status: 404 }),
    bookings: [],
    detect: null,
    checkOut: null,
    sessions: [],
  };
  setAuthSession({ accessToken: "staff-token", refreshToken: "staff-refresh", userProfile: { id: 2, username: "staff", role: "STAFF" } });
  fetchMock = vi.fn(async (input, options = {}) => {
    const url = new URL(input, "http://localhost");
    const path = url.pathname;
    const method = options.method || "GET";
    if (path.endsWith("/operations/zones")) return ok(model.zones);
    if (path.endsWith("/operations/passes/lookup")) return model.lookup(url.searchParams.get("plate"));
    if (path.endsWith("/operations/pass-entries") && method === "POST") return model.entry(JSON.parse(options.body));
    if (path.endsWith("/operations/sessions")) return ok(model.sessions);
    if (path.endsWith("/operations/layout")) return ok({ floors: [], slots: [] });
    if (path.includes("/parking-sessions/by-plate/")) return model.byPlate();
    if (path === "/api/v1/bookings") return ok({ items: model.bookings, totalCount: model.bookings.length, totalPages: model.bookings.length ? 1 : 0, pageNumber: 1, pageSize: 100 });
    if (path === "/api/v1/parking-sessions/active") return ok({ items: [], totalCount: 0, totalPages: 0 });
    if (path.endsWith("/operations/tickets/VE-0042")) return ok({ id: 10, ticketCode: "VE-0042", ticketStatus: "ISSUED" });
    if (path === "/api/v1/images") return ok({ id: 88, url: "https://images.example.invalid/gate.jpg" });
    if (path.endsWith("/recognition/detect")) return Response.json(model.detect);
    if (path.endsWith("/detections/501/confirm-exit"))
      return ok({ ...model.detect, confirmedAt: "2026-11-27T05:00:00Z", confirmedPlateNumber: "51F-123.45", matchedParkingSessionId: 10 });
    if (path === "/api/v1/parking-sessions/10" && method === "GET") return ok(model.session);
    if (path === "/api/v1/parking-sessions/10/check-out" && method === "POST") return model.checkOut(JSON.parse(options.body));
    return Response.json({ message: `unexpected ${method} ${path}` }, { status: 404 });
  });
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
  clearAuthSession();
});

const router = (ui) =>
  render(<MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>{ui}</MemoryRouter>);
const mountGate = (props = {}) => {
  const onChanged = vi.fn();
  // Các test của file này kiểm tra chế độ thủ công (Staff bấm xác nhận); chế độ tự động có file test riêng.
  router(<GateOperations lot={lot} revision={0} onChanged={onChanged} onBusyChange={() => {}} autoAdmit={false} {...props} />);
  return { onChanged };
};
const checkPlate = async (user, plate = "51F12345") => {
  await user.type(await screen.findByLabelText("Biển số xe"), plate);
  await user.click(screen.getByRole("button", { name: "Kiểm tra" }));
};
const noPass = (reason, message) => () => ok({ ...validPass, canEnter: false, reason, message });
const photo = () => new File(["photo"], "gate.jpg", { type: "image/jpeg" });
const fixedZoneReady = () => screen.findByRole("option", { name: "G · Tầng G vé tháng" });

describe("Gate screen: monthly pass entry", () => {
  it("checks the plate, then lets a valid pass holder in through the fixed zone", async () => {
    const user = userEvent.setup();
    const { onChanged } = mountGate();
    await checkPlate(user);

    expect(await screen.findByText(/Vé tháng còn hiệu lực/)).toBeTruthy();
    expect(screen.getByText(/Gói 1 tháng/)).toBeTruthy();
    expect(screen.getByText(/còn 25 ngày/)).toBeTruthy();
    expect(calls("GET", "/passes/lookup")[0][0]).toContain("plate=51F-123.45");
    expect(screen.queryByLabelText("Mã vé")).toBeNull();

    // Chỉ khu cố định được đề nghị; khu vãng lai không có trong danh sách.
    await fixedZoneReady();
    const zone = screen.getByLabelText("Khu vé tháng");
    expect([...zone.options].map((o) => o.textContent)).toEqual(["Chọn khu", "G · Tầng G vé tháng"]);
    expect(zone.value).toBe("11");

    await user.type(screen.getByLabelText("Ghi chú (không bắt buộc)"), "  xe quen  ");
    await user.click(screen.getByRole("button", { name: "Cho xe vào" }));

    expect(await screen.findByText(/Đã cho xe 51F-123.45 vào bằng vé tháng\. Lượt #77/)).toBeTruthy();
    const body = JSON.parse(calls("POST", "/pass-entries")[0][1].body);
    expect(body).toMatchObject({ zoneId: 11, plateNumber: "51F-123.45", imageId: null, note: "xe quen" });
    expect(body.eventId).toMatch(/[0-9a-f-]{36}/);
    expect(onChanged).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole("button", { name: "Tiếp nhận xe tiếp theo" }));
    expect(screen.getByLabelText("Biển số xe").value).toBe("");
    expect(screen.queryByText(/Đã cho xe/)).toBeNull();
  });

  it("saves the photo taken at the scan step with the pass entry", async () => {
    model.detect = { id: 501, plateNumber: "51F-123.45", confidence: 0.9 };
    const user = userEvent.setup();
    mountGate();
    await user.upload(await screen.findByLabelText(/Chọn \/ chụp ảnh/), photo());
    await waitFor(() => expect(screen.getByLabelText("Biển số xe").value).toBe("51F-123.45"));
    expect(String(calls("POST", "/recognition/detect")[0][0])).toContain("purpose=EXIT");
    await user.click(screen.getByRole("button", { name: "Kiểm tra" }));
    await fixedZoneReady();
    await user.click(screen.getByRole("button", { name: "Cho xe vào" }));
    await screen.findByText(/Lượt #77/);
    expect(JSON.parse(calls("POST", "/pass-entries")[0][1].body)).toMatchObject({ imageId: 88 });
  });

  it("keeps the same event id when the entry result is unknown and locks the scan step meanwhile", async () => {
    let attempts = 0;
    model.entry = (body) => {
      attempts += 1;
      return attempts === 1
        ? new Response("", { status: 503 })
        : ok({ session: { id: 77, plateNumber: body.plateNumber }, pass: validPass, replay: true });
    };
    const user = userEvent.setup();
    mountGate();
    await checkPlate(user);
    await fixedZoneReady();
    await user.click(screen.getByRole("button", { name: "Cho xe vào" }));

    const retry = await screen.findByRole("button", { name: "Gửi lại cùng lượt xe" });
    expect(screen.getByLabelText("Biển số xe").closest("fieldset").disabled).toBe(true);
    expect(screen.getByRole("button", { name: "Kiểm tra" }).disabled).toBe(true);
    await user.click(retry);
    expect(await screen.findByText(/đã được ghi nhận trước đó/)).toBeTruthy();

    const [first, second] = calls("POST", "/pass-entries").map(([, o]) => JSON.parse(o.body));
    expect(second).toEqual(first);
  });

  it("unlocks after the server clearly refuses, using a fresh event id next time", async () => {
    let attempts = 0;
    model.entry = (body) => {
      attempts += 1;
      return attempts === 1
        ? Response.json({ code: "ALREADY_INSIDE", message: "Xe này đang có một lượt gửi chưa kết thúc." }, { status: 409 })
        : ok({ session: { id: 78, plateNumber: body.plateNumber }, pass: validPass, replay: false });
    };
    const user = userEvent.setup();
    mountGate();
    await checkPlate(user);
    await fixedZoneReady();
    await user.click(screen.getByRole("button", { name: "Cho xe vào" }));
    expect(await screen.findByText("Xe này đang có một lượt gửi chưa kết thúc.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Kiểm tra" }).disabled).toBe(false);
    await user.click(screen.getByRole("button", { name: "Cho xe vào" }));
    await screen.findByText(/Lượt #78/);
    const [first, second] = calls("POST", "/pass-entries").map(([, o]) => JSON.parse(o.body));
    expect(second.eventId).not.toBe(first.eventId);
  });

  it("warns when the lot has no active fixed zone and blocks entry", async () => {
    model.zones = [zones[1]];
    const user = userEvent.setup();
    mountGate();
    await checkPlate(user);
    expect(await screen.findByText(/chưa có khu vé tháng đang hoạt động/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Cho xe vào" }).disabled).toBe(true);
  });

  it("still checks plates while the lot is closed but lets nobody in", async () => {
    const user = userEvent.setup();
    mountGate({ lot: { ...lot, isActive: false } });
    await checkPlate(user);
    expect((await screen.findByRole("button", { name: "Cho xe vào" })).disabled).toBe(true);
  });
});

describe("Gate screen: choosing the branch", () => {
  it("offers the walk-in form to a car without a pass", async () => {
    model.lookup = noPass("PASS_NOT_FOUND", "Không tìm thấy vé tháng của xe này tại bãi.");
    const user = userEvent.setup();
    mountGate();
    await checkPlate(user);
    expect(await screen.findByRole("heading", { name: "Cấp vé và tiếp nhận xe" })).toBeTruthy();
    expect(screen.getByText(/Xe vãng lai · 51F-123.45/)).toBeTruthy();
    expect(screen.getByText(/Nếu xe đang đi ra, hãy tra theo mã vé/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Cho xe vào" })).toBeNull();
  });

  it.each([
    ["PASS_EXPIRED", "Vé tháng của xe này đã hết hạn.", /Khách có thể gia hạn/],
    ["PASS_SUSPENDED", "Vé tháng của xe này đang bị tạm khóa.", /quản lý bãi trước/],
    ["PASS_UNPAID", "Vé tháng của xe này chưa được thanh toán.", /hoàn tất thanh toán/],
  ])("explains %s above the walk-in form", async (reason, message, hint) => {
    model.lookup = noPass(reason, message);
    const user = userEvent.setup();
    mountGate();
    await checkPlate(user);
    expect(await screen.findByText(message)).toBeTruthy();
    expect(screen.getByText(hint)).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Cấp vé và tiếp nhận xe" })).toBeTruthy();
    expect(calls("POST", "/pass-entries")).toHaveLength(0);
  });

  it("offers no entry at all when Core says the car is already inside", async () => {
    model.lookup = noPass("ALREADY_INSIDE", "Xe này đang có một lượt gửi chưa kết thúc.");
    const user = userEvent.setup();
    mountGate();
    await checkPlate(user);
    expect(await screen.findByText("Xe này đang có một lượt gửi chưa kết thúc.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Cho xe vào" })).toBeNull();
    expect(screen.queryByLabelText("Mã vé")).toBeNull();
  });

  it("goes to the exit branch for a car that is inside, without asking for a second photo", async () => {
    model.session = passSession;
    model.byPlate = () => ok(passSession);
    model.checkOut = () => ok({ ...passSession, status: "COMPLETED", checkOutTime: "2026-11-27T05:00:00Z" });
    const user = userEvent.setup();
    mountGate();
    await checkPlate(user);

    expect(await screen.findByText(/Xe vào bằng vé tháng — không thu phí/)).toBeTruthy();
    expect(calls("GET", "/passes/lookup")).toHaveLength(0);
    expect(screen.queryByLabelText("Biển số thực tế tại cổng ra")).toBeNull();
    expect(screen.getByText(/Biển số tại cổng ra:/)).toBeTruthy();
    await user.click(screen.getByRole("checkbox", { name: /Đã đối chiếu/ }));
    await user.click(screen.getByRole("button", { name: "Xác nhận xe đã ra bãi" }));
    await screen.findByText(/Phiên đã kết thúc/);

    await user.click(screen.getByRole("button", { name: "Tiếp nhận xe tiếp theo" }));
    expect(screen.getByLabelText("Biển số xe").value).toBe("");
    expect(screen.queryByText(/Phiên đã kết thúc/)).toBeNull();
  });

  it("asks staff to confirm an AI exit match before checking out", async () => {
    model.session = passSession;
    model.byPlate = () => ok(passSession);
    // AI tự khớp phiên ngay lúc quét nhưng chưa có ai xác nhận.
    model.detect = { id: 501, plateNumber: "51F-123.45", confirmedPlateNumber: "51F-123.45", matchedParkingSessionId: 10, confirmedAt: null };
    model.checkOut = () => ok({ ...passSession, status: "COMPLETED", checkOutTime: "2026-11-27T05:00:00Z" });
    const user = userEvent.setup();
    mountGate();
    await user.upload(await screen.findByLabelText(/Chọn \/ chụp ảnh/), photo());
    await waitFor(() => expect(screen.getByLabelText("Biển số xe").value).toBe("51F-123.45"));
    await user.click(screen.getByRole("button", { name: "Kiểm tra" }));
    await screen.findByText(/Xe vào bằng vé tháng — không thu phí/);
    await user.click(screen.getByRole("checkbox", { name: /Đã đối chiếu/ }));
    await user.click(screen.getByRole("button", { name: "Xác nhận xe đã ra bãi" }));
    await screen.findByText(/Phiên đã kết thúc/);
    expect(calls("POST", "/detections/501/confirm-exit")).toHaveLength(1);
    expect(JSON.parse(calls("POST", "/check-out")[0][1].body)).toMatchObject({
      exitRecognitionId: 501, checkOutImageUrl: "https://images.example.invalid/gate.jpg",
    });
  });

  it("does not treat a failed lookup as an arriving car", async () => {
    model.byPlate = () => Response.json({ message: "Máy chủ đang lỗi." }, { status: 500 });
    const user = userEvent.setup();
    mountGate();
    await checkPlate(user);
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(calls("GET", "/passes/lookup")).toHaveLength(0);
    expect(screen.queryByLabelText("Mã vé")).toBeNull();
  });

  it("rejects a malformed plate without calling the API", async () => {
    const user = userEvent.setup();
    mountGate();
    await checkPlate(user, "ABC");
    expect(calls("GET", "/passes/lookup")).toHaveLength(0);
  });

  it("forgets the previous branch when the plate is edited", async () => {
    const user = userEvent.setup();
    mountGate();
    await checkPlate(user);
    await screen.findByRole("button", { name: "Cho xe vào" });
    await user.type(screen.getByLabelText("Biển số xe"), "{backspace}9");
    expect(screen.queryByRole("button", { name: "Cho xe vào" })).toBeNull();
  });

  it("finds a car by ticket code and then asks for the exit gate plate", async () => {
    model.session = { ...passSession, verificationStatus: "VERIFIED" };
    const user = userEvent.setup();
    mountGate();
    await user.type(await screen.findByLabelText("Mã vé cần tìm"), "VE-0042");
    await user.click(screen.getByRole("button", { name: "Tìm xe đang gửi" }));
    expect(await screen.findByLabelText("Biển số thực tế tại cổng ra")).toBeTruthy();
    expect(screen.getByLabelText("Mã vé đã đối chiếu").value).toBe("VE-0042");
    expect(screen.getByRole("button", { name: /Thu tiền tại quầy/ })).toBeTruthy();
  });
});

const passSession = {
  id: 10, lotId: 1, lotName: "Bãi Trung Tâm", plateNumber: "51F-123.45", status: "ACTIVE",
  verificationStatus: "MONTHLY_PASS", checkInTime: "2026-11-27T01:00:00Z", totalFee: 0,
};

describe("Exit branch opened from a session link", () => {
  const mountExit = () =>
    router(<StaffExit lot={lot} revision={0} initialSessionId="10" onChanged={() => {}} onBusyChange={() => {}} />);

  it("shows that no fee is due and lets staff confirm the exit after matching the plate", async () => {
    model.session = passSession;
    model.checkOut = () => ok({ ...passSession, status: "COMPLETED", checkOutTime: "2026-11-27T05:00:00Z" });
    const user = userEvent.setup();
    mountExit();

    expect(await screen.findByText(/Xe vào bằng vé tháng — không thu phí/)).toBeTruthy();
    expect(screen.getByText("Vé tháng")).toBeTruthy();
    expect(screen.getByText("Không thu phí (vé tháng)")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Thu tiền tại quầy/ })).toBeNull();
    expect(screen.queryByText(/Chưa xác nhận thanh toán/)).toBeNull();
    expect(screen.queryByLabelText("Mã vé đã đối chiếu")).toBeNull();

    const input = await screen.findByLabelText("Biển số thực tế tại cổng ra");
    await user.type(input, "51F99999");
    await user.click(screen.getByRole("checkbox", { name: /Đã đối chiếu/ }));
    await user.click(screen.getByRole("button", { name: "Xác nhận xe đã ra bãi" }));
    await screen.findByText(/Biển số tại cổng ra không khớp/);
    expect(calls("POST", "/check-out")).toHaveLength(0);

    await user.clear(input);
    await user.type(input, "51F12345");
    await user.click(screen.getByRole("button", { name: "Xác nhận xe đã ra bãi" }));
    await screen.findByText(/Phiên đã kết thúc/);
    expect(JSON.parse(calls("POST", "/check-out")[0][1].body)).toEqual({ confirmedPlateNumber: "51F-123.45" });
  });

  it("still blocks a regular unpaid session and never offers the pass shortcut", async () => {
    model.session = { ...passSession, verificationStatus: "VERIFIED" };
    mountExit();
    expect(await screen.findByText(/Chưa xác nhận thanh toán/)).toBeTruthy();
    expect(screen.queryByText(/không thu phí\./)).toBeNull();
    expect(screen.getByRole("button", { name: "Xác nhận xe đã ra bãi" }).disabled).toBe(true);
    expect(screen.getByRole("checkbox", { name: /Đã đối chiếu/ }).disabled).toBe(true);
  });
});

describe("Staff menu", () => {
  it("has a single gate entry for cars coming in and going out", () => {
    const items = staffNavigationFor();
    expect(items[0]).toEqual(["/staff?view=entry", "Cổng: xe vào / xe ra"]);
    expect(items.filter(([href]) => /view=(entry|exit|pass-entry)$/.test(href))).toHaveLength(1);
  });
});

describe("Slot verification list", () => {
  it("never queues monthly pass sessions for slot verification", async () => {
    model.sessions = [
      { id: 21, lotId: 1, zoneId: 12, eventId: "e1", ticketCode: "VE-1", plateNumber: "30H-999.99", verificationStatus: "WAITING_VERIFICATION", status: "ACTIVE", revision: 1 },
      { id: 22, lotId: 1, zoneId: 11, eventId: "e2", ticketCode: null, plateNumber: "51F-123.45", verificationStatus: "MONTHLY_PASS", status: "ACTIVE", revision: 1 },
    ];
    router(<TicketVerification lot={lot} revision={0} onChanged={() => {}} onBusyChange={() => {}} />);
    expect(await screen.findByText(/VE-1 · #21/)).toBeTruthy();
    await waitFor(() => expect(screen.queryByText(/#22/)).toBeNull());
    expect(screen.queryByText(/51F-123.45/)).toBeNull();
  });
});
