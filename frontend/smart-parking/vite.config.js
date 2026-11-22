import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  base: "/app/",
  plugins: [react()],
  resolve: {
    preserveSymlinks: true,
  },
  server: {
    proxy: {
      "/api": "http://localhost:5295",
      "/ops-api": {
        target: "http://localhost:5174",
        rewrite: (p) => p.replace(/^\/ops-api/, ""),
      },
    },
  },
  build: {
    outDir: path.resolve(__dirname, "../../backend/src/SmartParking.Api/wwwroot/app"),
    emptyOutDir: true,
  },
});
