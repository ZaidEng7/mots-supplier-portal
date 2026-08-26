import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { compression } from 'vite-plugin-compression2'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    // Pre-compress build output so a static host that serves precompiled files
    // (nginx gzip_static, most CDNs) doesn't have to gzip on every request - and so
    // Lighthouse CI measures the same payload size real users get, not an
    // uncompressed dev-preview response (docs: LCP < 2.5s on mid-range mobile/4G).
    compression({ algorithms: ['gzip'], include: /\.(js|css|html|svg)$/ }),
  ],
  server: {
    port: 5173,
  },
})
