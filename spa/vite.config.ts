import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Dev server port 3000 matches the SPA redirect URI http://localhost:3000.
// Build output goes to dist/ so the deploy workflow can publish a static bundle.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
  },
  build: {
    outDir: "dist",
  },
});
