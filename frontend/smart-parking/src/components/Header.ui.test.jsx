import { afterEach, expect, it } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import Header from "./Header.jsx";
import { clearAuthSession } from "../services/authSession.js";

afterEach(() => {
  cleanup();
  clearAuthSession();
});

it("opens the mobile navigation and closes it with Escape or navigation", async () => {
  render(
    <MemoryRouter initialEntries={["/"]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <Header />
      <Routes>
        <Route path="/" element={<h1>Trang chủ kiểm thử</h1>} />
        <Route path="/parking" element={<h1>Bãi đỗ kiểm thử</h1>} />
      </Routes>
    </MemoryRouter>,
  );

  const toggle = screen.getByRole("button", { name: "Mở menu điều hướng" });
  const navigation = screen.getByRole("navigation", { name: "Điều hướng chính" });

  expect(toggle.getAttribute("aria-expanded")).toBe("false");
  fireEvent.click(toggle);
  expect(toggle.getAttribute("aria-expanded")).toBe("true");
  expect(navigation.classList.contains("is-mobile-open")).toBe(true);

  fireEvent.keyDown(document, { key: "Escape" });
  expect(screen.getByRole("button", { name: "Mở menu điều hướng" }).getAttribute("aria-expanded")).toBe("false");

  fireEvent.click(screen.getByRole("button", { name: "Mở menu điều hướng" }));
  fireEvent.click(screen.getByRole("link", { name: "Xem bãi đỗ" }));
  expect(await screen.findByRole("heading", { name: "Bãi đỗ kiểm thử" })).toBeTruthy();
  expect(screen.getByRole("button", { name: "Mở menu điều hướng" }).getAttribute("aria-expanded")).toBe("false");
});
