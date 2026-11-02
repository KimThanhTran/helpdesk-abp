import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// In development the API is reached through the dev server, so the browser sees one origin and no CORS is needed.
const target = process.env.API_TARGET || 'http://localhost:8080';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': target,
      '/connect': target,
      '/signalr-hubs': { target, ws: true },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test/setup.js',
  },
});
