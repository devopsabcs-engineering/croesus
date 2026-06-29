import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MsalProvider } from "@azure/msal-react";
import { pca } from "./authConfig";
import App from "./App";

const root = document.getElementById("root");
if (!root) {
  throw new Error("Root element #root not found.");
}

// Initialize MSAL before rendering so the redirect promise is handled cleanly.
pca.initialize().then(() => {
  createRoot(root).render(
    <StrictMode>
      <MsalProvider instance={pca}>
        <App />
      </MsalProvider>
    </StrictMode>
  );
});
