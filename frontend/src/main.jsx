import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { HashRouter } from 'react-router-dom';
import App from './App';
import { AuthProvider } from './auth';
import { LanguageProvider } from './i18n';
import { NotificationProvider } from './notifications';
import './styles.css';

// HashRouter so the app works under any path (GitHub Pages serves it from /helpdesk-abp/) without server rewrites.
createRoot(document.getElementById('root')).render(
  <StrictMode>
    <LanguageProvider>
      <HashRouter>
        <AuthProvider>
          <NotificationProvider>
            <App />
          </NotificationProvider>
        </AuthProvider>
      </HashRouter>
    </LanguageProvider>
  </StrictMode>
);
