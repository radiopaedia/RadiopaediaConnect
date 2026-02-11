import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import jsconfigPaths from 'vite-jsconfig-paths'
import eslint from 'vite-plugin-eslint';
import svgr from 'vite-plugin-svgr'
import tailwindcss from "@tailwindcss/vite";
import basicSsl from '@vitejs/plugin-basic-ssl';
import path from 'path';

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
    resolve: {
        alias: {
            // cornerstone-tools v3 is a legacy CJS package whose package.json
            // does not declare proper main/module/exports fields for Vite 7.
            // Point directly at the pre-built dist bundle to avoid resolution failures.
            'cornerstone-tools': path.resolve(
                __dirname,
                'node_modules/cornerstone-tools/dist/cornerstoneTools.min.js'
            ),
        }
    },
    build: {
        outDir: '../wwwroot',
        emptyOutDir: true,
        sourcemap: true
    },
    server: {
        host: true,
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
                changeOrigin: false, // Keep false so Backend sees 172.x.x.x Host header
                secure: false,
            }
        }
    }
})