import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import BookingWorkflowDemo from "./BookingWorkflowDemo.jsx";
import { bookingWorkflowStore } from "../services/bookingWorkflowStore.js";

const storageKey = "smart-parking.booking-workflow.v1";

function resetWorkflow() {
  window.localStorage.removeItem(storageKey);
  window.dispatchEvent(new StorageEvent("storage", { key: storageKey }));
}

describe("BookingWorkflowDemo", () => {
  beforeEach(resetWorkflow);

  it("carries a booking through gate check-in and parking with the plate photo", async () => {
    render(<BookingWorkflowDemo />);

    fireEvent.change(screen.getByLabelText("Họ và tên"), {
      target: { value: "Nguyễn An" },
    });
    fireEvent.change(screen.getByLabelText("Số điện thoại"), {
      target: { value: "0901234567" },
    });
    fireEvent.change(screen.getByLabelText("Biển số xe"), {
      target: { value: "51a-123.45" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Xác nhận đặt chỗ" }));

    expect(await screen.findByText("PK-001")).toBeTruthy();
    expect(screen.getByText(/^(29|30):\d{2}$/)).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: /Staff Cổng/ }));
    fireEvent.click(
      await screen.findByRole("button", {
        name: "Xác nhận xe đến & Chuyển Điều phối",
      }),
    );
    expect((await screen.findByRole("status")).textContent).toContain(
      "PK-001 sang hàng chờ Điều phối",
    );

    fireEvent.click(screen.getByRole("button", { name: /Staff Điều phối/ }));
    fireEvent.change(screen.getByLabelText("Màu xe"), {
      target: { value: "Trắng" },
    });
    fireEvent.change(screen.getByLabelText("Vị trí ô đỗ"), {
      target: { value: "Khu A - Ô 12" },
    });
    fireEvent.change(screen.getByLabelText("Ghi chú tình trạng xe / khách hàng"), {
      target: { value: "Trầy nhẹ cửa bên phải" },
    });
    const imageInput = screen.getByLabelText(/Ảnh biển số xe/);
    fireEvent.change(imageInput, {
      target: {
        files: [new File(["plate image"], "plate.png", { type: "image/png" })],
      },
    });
    expect(await screen.findByAltText("Ảnh biển số xe 51A-123.45")).toBeTruthy();
    const saveForm = screen.getByRole("button", {
      name: "Lưu thông tin & Xác nhận đã đỗ bãi",
    }).closest("form");
    fireEvent.submit(saveForm);
    await waitFor(() =>
      expect(bookingWorkflowStore.getSnapshot().bookings[0].status).toBe("PARKED"),
    );

    fireEvent.click(await screen.findByRole("tab", { name: /Xe đang đỗ/ }));
    const parkedCard = await screen.findByText(/Khu A - Ô 12/);
    const card = parkedCard.closest("article");
    expect(within(card).getByText("51A-123.45")).toBeTruthy();
    expect(within(card).getByText("Trầy nhẹ cửa bên phải")).toBeTruthy();
    expect(within(card).getByRole("img", { name: "Biển số 51A-123.45" })).toBeTruthy();

    await waitFor(() => {
      const saved = JSON.parse(window.localStorage.getItem(storageKey));
      expect(saved[0]).toMatchObject({
        id: "PK-001",
        status: "PARKED",
        paymentMethod: "QR_CODE",
        parkingSlot: "Khu A - Ô 12",
        vehicleColor: "Trắng",
        vehicleNotes: "Trầy nhẹ cửa bên phải",
      });
      expect(saved[0].plateImageUrl).toContain("data:image/png;base64,");
    });
  });

  it("automatically cancels a pending booking at its 30-minute expiry", () => {
    const booking = bookingWorkflowStore.createBooking(
      {
        customerName: "Nguyễn An",
        phoneNumber: "0901234567",
        licensePlate: "51A-123.45",
        vehicleType: "Ô tô",
        paymentMethod: "VNPAY",
      },
      1_000,
    );

    bookingWorkflowStore.expireDueBookings(1_000 + 30 * 60_000);

    expect(bookingWorkflowStore.getSnapshot().bookings[0]).toMatchObject({
      id: booking.id,
      expireTime: new Date(1_000 + 30 * 60_000).toISOString(),
      status: "CANCELLED",
    });
  });
});
