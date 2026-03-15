import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './app'
import './styles/app.css'

// Ensure theme is applied before first paint
const savedTheme = (localStorage.getItem('theme') || 'light')
document.documentElement.setAttribute('data-theme', savedTheme)

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
)
