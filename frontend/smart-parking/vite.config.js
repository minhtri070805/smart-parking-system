import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  base: "/app/",
  plugins: [react()],
  server: {
    proxy: {
      "/api": "http://localhost:5295",
      "/ops-api": {
        target: "http://localhost:5174",
        rewrite: (path) => path.replace(/^\/ops-api/, ""),
      },
    },
  },
  build: {
    outDir: "../../backend/src/SmartParking.Api/wwwroot/app",
    emptyOutDir: true,
  },
});
