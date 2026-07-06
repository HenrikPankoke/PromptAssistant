/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        bg: 'var(--bg)',
        surface: 'var(--surface)',
        elevated: 'var(--elevated)',
        hover: 'var(--hover)',
        selected: 'var(--selected)',
        border: 'var(--border)',
        'border-strong': 'var(--border-strong)',
        text: 'var(--text)',
        'text-muted': 'var(--text-muted)',
        'text-faint': 'var(--text-faint)',
        accent: 'var(--accent)',
        'accent-hover': 'var(--accent-hover)',
        'accent-soft': 'var(--accent-soft)',
        'accent-text': 'var(--accent-text)',
        danger: 'var(--danger)',
        'danger-soft': 'var(--danger-soft)',
      },
      fontFamily: {
        sans: [
          'Inter Variable',
          'Inter',
          'Segoe UI Variable Text',
          'Segoe UI',
          'system-ui',
          'sans-serif',
        ],
        mono: ['ui-monospace', 'Cascadia Code', 'Consolas', 'monospace'],
      },
      borderRadius: {
        lg: '10px',
        xl: '14px',
        '2xl': '18px',
      },
      boxShadow: {
        soft: '0 1px 2px rgba(0,0,0,0.04), 0 1px 3px rgba(0,0,0,0.06)',
        pop: '0 10px 40px rgba(0,0,0,0.14)',
      },
      keyframes: {
        'fade-in': {
          '0%': { opacity: '0', transform: 'translateY(4px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' },
        },
        'blink': {
          '0%, 100%': { opacity: '1' },
          '50%': { opacity: '0.25' },
        },
      },
      animation: {
        'fade-in': 'fade-in 0.18s ease-out',
        'blink': 'blink 1s ease-in-out infinite',
      },
    },
  },
  plugins: [],
}
