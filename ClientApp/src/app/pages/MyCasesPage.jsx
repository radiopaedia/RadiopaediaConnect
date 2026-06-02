import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router';
import MainLayout from './MainLayout';
import LoginPage from './LoginPage';
import CasesTable from './components/CasesTable';

const MyCasesPage = () => {
    const navigate = useNavigate();
    const [user, setUser] = useState(null);
    const [authLoading, setAuthLoading] = useState(true);
    const [cases, setCases] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);

    useEffect(() => {
        fetch('/api/auth/me')
            .then(res => res.ok ? res.json() : null)
            .then(data => setUser(data ? { name: data.username, ...data } : null))
            .catch(() => setUser(null))
            .finally(() => setAuthLoading(false));
    }, []);

    useEffect(() => {
        if (user) fetchCases();
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [user]);

    const fetchCases = async () => {
        setLoading(true);
        setError(null);
        try {
            const res = await fetch('/api/cases/my-cases');
            if (!res.ok) throw new Error(res.status === 401 ? 'Please log in to view your cases.' : 'Failed to fetch cases');
            setCases(await res.json());
        } catch (err) {
            setError(err.message);
        } finally {
            setLoading(false);
        }
    };

    const handleLogout = async () => {
        try {
            await fetch('/api/auth/logout', { method: 'POST' });
            setUser(null);
            window.location.href = '/';
        } catch (err) {
            console.error('Logout failed', err);
        }
    };

    if (authLoading) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-white dark:bg-slate-900">
                <div className="text-lg text-slate-600">Loading...</div>
            </div>
        );
    }

    if (!user) return <LoginPage />;

    const addNewCaseButton = (
        <button
            onClick={() => navigate('/')}
            className="flex items-center gap-2 px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition-colors text-sm"
        >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4v16m8-8H4" />
            </svg>
            Add New Case
        </button>
    );

    return (
        <MainLayout user={user} onLogout={handleLogout}>
            <div className="p-6 max-w-6xl mx-auto">
                <div className="mb-6">
                    <h1 className="text-2xl font-bold text-slate-900 dark:text-white">My Cases</h1>
                    <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">View and track your submitted cases</p>
                </div>
                <CasesTable
                    cases={cases}
                    loading={loading}
                    error={error}
                    onRefresh={fetchCases}
                    loadingMessage="Loading your cases..."
                    actions={addNewCaseButton}
                />
            </div>
        </MainLayout>
    );
};

export default MyCasesPage;
