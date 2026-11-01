import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react(), {
    name: 'local-api-origin',
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        const origin = request.headers.origin
        if (request.url?.startsWith('/api/') && origin && !['http://127.0.0.1:5173', 'http://localhost:5173'].includes(origin)) {
          response.statusCode = 403
          response.end('Origem não permitida.')
          return
        }
        next()
      })
    },
  }],
  server: {
    port: 5173,
    host: '127.0.0.1',
    strictPort: true,
    proxy: { '/api': { target: 'http://127.0.0.1:8000', changeOrigin: true, configure(proxy) { proxy.on('proxyReq', request => { request.removeHeader('origin') }) } } },
  },
  resolve: {
    alias: {
      '@': '/src',
    },
  },
})
