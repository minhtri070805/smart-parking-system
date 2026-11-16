import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it } from "vitest";
import PasswordInput from "./PasswordInput.jsx";

it("toggles password visibility without clearing the entered value", () => {
  render(
    <PasswordInput
      id="password"
      name="password"
      value="example-password"
      onChange={() => {}}
    />,
  );
  const input = document.getElementById("password");

  expect(input.type).toBe("password");
  fireEvent.click(screen.getByRole("button", { name: "Hiện mật khẩu" }));

  expect(input.type).toBe("text");
  expect(input.value).toBe("example-password");
  expect(screen.getByRole("button", { name: "Ẩn mật khẩu" }).getAttribute("aria-pressed")).toBe("true");
});
