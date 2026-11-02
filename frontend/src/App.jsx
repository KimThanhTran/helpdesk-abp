import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth';
import { useT } from './i18n';
import Layout from './components/Layout';
import Login from './pages/Login';
import { MyTickets, Queue } from './pages/Lists';
import NewTicket from './pages/NewTicket';
import Reports from './pages/Reports';
import Ticket from './pages/Ticket';

export default function App() {
  const { t } = useT();
  const { token, user, loading } = useAuth();
  if (loading) return <p className="center">{t('app.loading')}</p>;
  if (!token || !user) return <Login />;

  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<MyTickets />} />
        <Route path="new" element={<NewTicket />} />
        <Route path="tickets/:id" element={<Ticket />} />
        {user.isAgent ? <Route path="queue" element={<Queue />} /> : null}
        {user.isAgent ? <Route path="reports" element={<Reports />} /> : null}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
