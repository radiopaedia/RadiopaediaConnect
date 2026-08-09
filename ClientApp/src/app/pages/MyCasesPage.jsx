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
    const [syncing, setSyncing] = useState(false);
    const [syncSummary, setSyncSummary] = useState(null);

    useEffect(() => {
        fetch('/api/auth/me')
            .then(res => res.ok ? res.json() : null)
            .then(data => setUser(data ? { name: data.username, ...data } : null))
            .catch(() => setUser(null))
            .finally(() => setAuthLoading(false));
    }, []);

    useEffect(() => {
        if (user) fetchCases({ thenSync: true });
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [user]);

    const fetchCases = async ({ thenSync = false } = {}) => {
        setLoading(true);
        setError(null);
        try {
            const res = await fetch('/api/cases/my-cases');
            if (!res.ok) throw new Error(res.status === 401 ? 'Please log in to view your cases.' : 'Failed to fetch cases');
            const data = await res.json();
            setCases(data);

            // Only worth asking Radiopaedia about cases that actually made it there
            if (thenSync && data.some(c => c.radiopaediaCaseId)) {
                syncWithRadiopaedia({ silent: true });
            }
        } catch (err) {
            setError(err.message);
        } finally {
            setLoading(false);
        }
    };

    // Asks Radiopaedia which of our uploaded cases still exist and which are still drafts,
    // then refreshes the table with what came back. The silent form runs on page load, where
    // a Radiopaedia outage should not put an error banner over a perfectly good case list.
    const syncWithRadiopaedia = async ({ silent = false } = {}) => {
        setSyncing(true);
        if (!silent) {
            setError(null);
            setSyncSummary(null);
        }
        try {
            const res = await fetch('/api/cases/reconcile', { method: 'POST' });
            const data = await res.json().catch(() => null);
            if (!res.ok) throw new Error(data?.message || 'Failed to sync with Radiopaedia');
            setCases(data.cases);
            if (!silent) setSyncSummary(data.summary);
        } catch (err) {
            if (silent) {
                console.warn('Background sync with Radiopaedia failed:', err.message);
            } else {
                setError(err.message);
            }
        } finally {
            setSyncing(false);
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

    const toolbarActions = (
        <>
            <button
                onClick={() => syncWithRadiopaedia()}
                disabled={syncing || loading}
                className="flex items-center gap-2 px-4 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800 transition-colors disabled:opacity-50 text-sm"
                title="Check which of your cases still exist on Radiopaedia and which are still drafts"
            >
                <svg className={`w-4 h-4 ${syncing ? 'animate-spin' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                </svg>
                {syncing ? 'Syncing...' : 'Sync with Radiopaedia'}
            </button>
            <button
                onClick={() => navigate('/')}
                className="flex items-center gap-2 px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition-colors text-sm"
            >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4v16m8-8H4" />
                </svg>
                Add New Case
            </button>
        </>
    );

    return (
        <MainLayout user={user} onLogout={handleLogout}>
            <div className="p-6 max-w-6xl mx-auto">
                <div className="mb-6">
                    <h1 className="text-2xl font-bold text-slate-900 dark:text-white">My Cases</h1>
                    <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">View and track your submitted cases</p>
                </div>
                {syncSummary && (
                    <div className="mb-4 rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800/60 px-4 py-3 text-sm text-slate-600 dark:text-slate-300">
                        Checked {syncSummary.localCasesChecked} uploaded case{syncSummary.localCasesChecked === 1 ? '' : 's'} against
                        {' '}{syncSummary.remoteCaseCount} case{syncSummary.remoteCaseCount === 1 ? '' : 's'} on Radiopaedia:
                        {' '}{syncSummary.draftCount} draft, {syncSummary.pendingReviewCount} in review,
                        {' '}{syncSummary.publishedCount} published
                        {syncSummary.deletedCount > 0 && (
                            <span className="text-red-600 dark:text-red-400">
                                , {syncSummary.deletedCount} no longer on Radiopaedia
                            </span>
                        )}
                        .
                    </div>
                )}
                <CasesTable
                    cases={cases}
                    loading={loading}
                    error={error}
                    onRefresh={() => fetchCases({ thenSync: true })}
                    loadingMessage="Loading your cases..."
                    actions={toolbarActions}
                    onAddToCase={(c) => navigate(`/?appendTo=${c.id}`)}
                />
            </div>
        </MainLayout>
    );
};

export default MyCasesPage;
