import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import ParkingCanvas from "./ParkingCanvas.jsx";

it("shows the list fallback guidance when the 3D renderer cannot load", async () => {
  const definition = {
    id: "1",
    name: "Bãi A",
    liveFloors: [{ id: 10, name: "Tầng 1", slots: [] }],
  };

  render(<ParkingCanvas
    definition={definition}
    floorId={10}
    role="ADMIN"
    onSelect={vi.fn()}
    onFloor={vi.fn()}
    viewerRef={{ current: null }}
    rendererLoader={() => Promise.reject(new Error("WebGL unavailable"))}
  />);

  expect((await screen.findByRole("alert")).textContent).toContain("Bạn vẫn có thể xem và thao tác bằng danh sách ô bên cạnh");
  expect(screen.getByLabelText("Mô hình bãi đỗ 3D")).toBeTruthy();
});
