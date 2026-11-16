import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import Login from "./Login.jsx";
import { clearAuthSession, getAuthSession } from "../services/authSession.js";

afterEach(() => {
  cleanup();
  clearAuthSession();
  vi.unstubAllGlobals();
});

it("allows the login password to be shown and hidden", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({ data: { enabled: false } })));

  render(<MemoryRouter initialEntries={["/login"]} future={{v7_startTransition:true,v7_relativeSplatPath:true}}>
    <Routes><Route path="/login" element={<Login />} /></Routes>
  </MemoryRouter>);

  const password = document.getElementById("login-password");
  await screen.findByRole("button", { name: "Hiện mật khẩu" });
  expect(password.type).toBe("password");
  fireEvent.click(screen.getByRole("button", { name: "Hiện mật khẩu" }));
  expect(password.type).toBe("text");
  fireEvent.click(screen.getByRole("button", { name: "Ẩn mật khẩu" }));
  expect(password.type).toBe("password");
});

it("exchanges Google's credential and challenge for an application session", async () => {
  const initialize = vi.fn();
  vi.stubGlobal("google", { accounts: { id: { initialize, renderButton: vi.fn() } } });
  const session = { accessToken: "core-access", refreshToken: "core-refresh", userProfile: { role: "USER" } };
  const challenge = { enabled: true, clientId: "test.apps.googleusercontent.com", nonce: "challenge" };
  const fetcher = vi.fn(async url => Response.json({
    data: url.endsWith("/challenge") ? challenge : session,
  }));
  vi.stubGlobal("fetch", fetcher);

  render(<MemoryRouter initialEntries={["/login"]} future={{v7_startTransition:true,v7_relativeSplatPath:true}}>
    <Routes><Route path="/login" element={<Login />} /><Route path="/customer" element={<h1>Khách</h1>} /></Routes>
  </MemoryRouter>);

  await screen.findByText("hoặc");
  await vi.waitFor(() => expect(initialize).toHaveBeenCalledTimes(1));
  const options = initialize.mock.calls[0][0];
  expect(options.nonce).toBe("challenge");
  await act(async () => options.callback({ credential: "google-id-token" }));

  expect(fetcher).toHaveBeenCalledWith("/api/v1/auth/google", expect.objectContaining({
    method: "POST",
    body: JSON.stringify({ credential: "google-id-token", nonce: "challenge" }),
  }));
  expect(getAuthSession()).toEqual(session);
  await screen.findByRole("heading", { name: "Khách" });
});
