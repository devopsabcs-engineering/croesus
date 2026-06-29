/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_SPA_CLIENT_ID: string;
  readonly VITE_TENANT_ID: string;
  readonly VITE_API_SCOPE: string;
  readonly VITE_API_BASE_URL: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
