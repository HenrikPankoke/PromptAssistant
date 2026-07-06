import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import './styles.css'

// The host passes the saved theme in the URL so the correct CSS variables apply
// before first paint — without this the page flashes light before state loads.
if (new URLSearchParams(window.location.search).get('theme') === 'Dark') {
  document.documentElement.classList.add('dark')
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
)
