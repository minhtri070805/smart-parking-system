import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it } from "vitest";
import { MemoryRouter } from "react-router-dom";
import Register from "./Register.jsx";

it("toggles registration passwords independently", () => {
  render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <Register />
    </MemoryRouter>,
  );

  const password = document.getElementById("register-password");
  const confirmation = document.getElementById("register-confirm");
  const [passwordToggle, confirmationToggle] = screen.getAllByRole("button", { name: "Hiện mật khẩu" });

  fireEvent.click(passwordToggle);
  expect(password.type).toBe("text");
  expect(confirmation.type).toBe("password");

  fireEvent.click(confirmationToggle);
  expect(password.type).toBe("text");
  expect(confirmation.type).toBe("text");
});
