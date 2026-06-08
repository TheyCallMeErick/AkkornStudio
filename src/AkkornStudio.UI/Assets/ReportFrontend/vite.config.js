import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig({
  plugins: [vue()],
  define: {
    "process.env.NODE_ENV": JSON.stringify("production"),
    "process.env": "{}"
  },
  build: {
    emptyOutDir: true,
    outDir: "dist",
    cssCodeSplit: false,
    lib: {
      entry: "src/main.js",
      name: "AkkornReportApp",
      formats: ["iife"],
      fileName: () => "report-app.js"
    },
    rollupOptions: {
      output: {
        assetFileNames: (assetInfo) => {
          if (assetInfo.name?.endsWith(".css")) {
            return "report-app.css";
          }

          return "report-app.[ext]";
        }
      }
    }
  }
});
