import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import ParkingAdminAssistant from "./ParkingAdminAssistant.jsx";

const slots = [
  { id: 1, code: "A-01", status: "EMPTY", isProtected: false },
  { id: 2, code: "A-02", status: "OCCUPIED", isProtected: true },
];

function analyze(command) {
  fireEvent.change(screen.getByLabelText("Câu lệnh"), { target: { value: command } });
  fireEvent.click(screen.getByRole("button", { name: "Phân tích lệnh" }));
}

it("answers a read command without preparing or executing a write", () => {
  const onConfirm = vi.fn();
  render(<ParkingAdminAssistant slots={slots} disabled={false} onConfirm={onConfirm} />);

  analyze("xem ô A-01");

  expect(screen.getByRole("status").textContent).toContain("A-01 hiện ở trạng thái Trống");
  expect(screen.queryByRole("button", { name: "Xác nhận thay đổi" })).toBeNull();
  expect(onConfirm).not.toHaveBeenCalled();
});

it("requires an explicit reason and confirmation before changing a slot", async () => {
  const onConfirm = vi.fn().mockResolvedValue({ message: "Đã cập nhật ô đỗ." });
  render(<ParkingAdminAssistant slots={slots} disabled={false} onConfirm={onConfirm} />);

  analyze("đổi ô A-01 sang bảo trì");

  expect(screen.getByText("Chưa có dữ liệu nào được ghi.")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Xác nhận thay đổi" }).disabled).toBe(true);
  expect(onConfirm).not.toHaveBeenCalled();

  fireEvent.change(screen.getByLabelText("Lý do xác nhận"), { target: { value: "Kiểm tra cảm biến" } });
  fireEvent.click(screen.getByRole("button", { name: "Xác nhận thay đổi" }));

  await waitFor(() => expect(onConfirm).toHaveBeenCalledWith({
    slot: slots[0],
    status: "MAINTENANCE",
    reason: "Trợ lý Admin: Kiểm tra cảm biến",
  }));
});

it("refuses to prepare a write for a protected slot", () => {
  const onConfirm = vi.fn();
  render(<ParkingAdminAssistant slots={slots} disabled={false} onConfirm={onConfirm} />);

  analyze("đổi ô A-02 sang bảo trì");

  expect(screen.getByRole("status").textContent).toContain("đang gắn với xe hoặc điều phối");
  expect(screen.queryByRole("button", { name: "Xác nhận thay đổi" })).toBeNull();
  expect(onConfirm).not.toHaveBeenCalled();
});

it("keeps confirmation disabled while the authoritative snapshot is stale", () => {
  render(<ParkingAdminAssistant slots={slots} disabled onConfirm={vi.fn()} />);

  analyze("đổi ô A-01 sang bảo trì");
  fireEvent.change(screen.getByLabelText("Lý do xác nhận"), { target: { value: "Kiểm tra cảm biến" } });

  expect(screen.getByRole("button", { name: "Xác nhận thay đổi" }).disabled).toBe(true);
  expect(screen.getByText("Trợ lý đang ở chế độ chỉ đọc vì snapshot chưa đồng bộ.")).toBeTruthy();
});
