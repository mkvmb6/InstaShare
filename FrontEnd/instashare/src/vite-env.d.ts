/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Where shares are served from. Defaults to the production CDN. */
  readonly VITE_CDN_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
