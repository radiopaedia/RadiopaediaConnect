import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import jsconfigPaths from 'vite-jsconfig-paths'
import eslint from 'vite-plugin-eslint';
import svgr from 'vite-plugin-svgr'
import tailwindcss from "@tailwindcss/vite";
import basicSsl from '@vitejs/plugin-basic-ssl';
import path from 'path'; // [Added] Import path to help resolve directories

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [
        react(),
        jsconfigPaths(),
        svgr(),
        eslint(),
        tailwindcss(),
        basicSsl() 
    ],
    // [Added] Build configuration for ASP.NET Core integration
    build: {
        outDir: '../wwwroot', // Output to project root/wwwroot
        emptyOutDir: true,    // Clears the folder before building
        sourcemap: true       // Optional: Helpful for debugging production builds
    },
    server: {
        host: true, // Listen on all local IPs (0.0.0.0)
        port: 5173,
        https: false,
        proxy: {
            '/api': {
                target: 'http://localhost:5198',
                changeOrigin: false,
                secure: false,
            },
            '/signin-radiopaedia': {
                target: 'http://localhost:5198',
                changeOrigin: false, // [CRITICAL] Keep false so Backend sees 172.x.x.x Host header
                secure: false,
            }
        }
    }
})