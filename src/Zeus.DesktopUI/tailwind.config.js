/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        zeus: {
          50:  '#f0f4ff',
          100: '#dce6ff',
          200: '#b8ccff',
          300: '#8aaaff',
          400: '#5b84ff',
          500: '#3b5bff',   // main accent
          600: '#2a3fd9',
          700: '#2232b0',
          800: '#1f2d8f',
          900: '#1e2a74',
          950: '#12184a',
        },
        surface: {
          DEFAULT: '#0f1117',
          50:  '#181b24',
          100: '#1e222d',
          200: '#262b38',
          300: '#313746',
        }
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
      },
      boxShadow: {
        'glow': '0 0 20px -5px rgba(59, 91, 255, 0.25)',
        'card': '0 1px 3px 0 rgb(0 0 0 / 0.3), 0 1px 2px -1px rgb(0 0 0 / 0.3)',
      }
    },
  },
  plugins: [],
}
