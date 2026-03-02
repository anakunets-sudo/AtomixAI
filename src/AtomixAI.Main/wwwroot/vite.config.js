import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    lib: {
      entry: './main.js', // Ваш файл со скриптом
      name: 'AtomicEditor',
      fileName: 'atomix-bundle',
      formats: ['iife'], // Формат для прямого подключения через <script>
    },
    outDir: 'dist',
    minify: 'terser',
  },
});
