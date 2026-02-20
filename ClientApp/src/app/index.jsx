import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';

const HomePage = () => {
    const navigate = useNavigate();
    const [user, setUser] = useState(null);
    const [loading, setLoading] = useState(true);
    const [settingsStatus, setSettingsStatus] = useState(null);

    // Capture query params for auto-search
    useEffect(() => {
        const params = new URLSearchParams(window.location.search);
        const accession = params.get('accession');
        const nodeName = params.get('node.name') || params.get('node');

        if (accession) {
            localStorage.setItem('rp_auto_accession', accession);
        }
        if (nodeName) {
            localStorage.setItem('rp_auto_node', nodeName);
        }
    }, []);

    // Check settings status
    useEffect(() => {
        const checkSettings = async () => {
            try {
                const res = await fetch('/api/settings/status');
                if (res.ok) {
                    const data = await res.json();
                    setSettingsStatus(data);
                }
            } catch (err) {
                console.error('Settings status check failed', err);
            }
        };
        checkSettings();
    }, []);

    // Check authentication status on mount
    useEffect(() => {
        fetch('/api/auth/me')
            .then((res) => {
                if (res.ok) return res.json();
                return null;
            })
            .then((data) => {
                if (data) {
                    setUser({ name: data.username, ...data });
                } else {
                    setUser(null);
                }
            })
            .catch((err) => {
                console.error('Auth check failed', err);
                setUser(null);
            })
            .finally(() => setLoading(false));
    }, []);

    const handleLogout = async () => {
        try {
            await fetch('/api/auth/logout', { method: 'POST' });
            setUser(null);
            window.location.reload();
        } catch (error) {
            console.error('Logout failed', error);
        }
    };

    const handleSetupClick = () => {
        navigate('/setup');
    };

    if (loading) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-white dark:bg-slate-900">
                <div className="text-lg text-slate-600">Loading...</div>
            </div>
        );
    }

    // Show Dashboard if logged in
    if (user) {
        return (
            <DashboardPage
                user={user}
                onLogout={handleLogout}
                settingsStatus={settingsStatus}
            />
        );
    }

    // Render Login View with optional setup banner
    return (
        <>
            {settingsStatus && !settingsStatus.isPasswordSet && (
                <div className="fixed top-0 left-0 right-0 z-50 bg-amber-500 text-white">
                    <div className="container mx-auto px-4 py-3 flex items-center justify-between">
                        <div className="flex items-center gap-2">
                            <svg className="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L4.082 16.5c-.77.833.192 2.5 1.732 2.5z" />
                            </svg>
                            <span className="text-sm font-medium">
                                Admin password has not been set up yet. Complete the initial setup to configure this application.
                            </span>
                        </div>
                        <button
                            onClick={handleSetupClick}
                            className="flex-shrink-0 px-4 py-1.5 bg-white text-amber-700 text-sm font-semibold rounded-md hover:bg-amber-50 transition-colors"
                        >
                            Begin Setup
                        </button>
                    </div>
                </div>
            )}
            {settingsStatus && settingsStatus.isPasswordSet && !settingsStatus.isConfigured && (
                <div className="fixed top-0 left-0 right-0 z-50 bg-orange-500 text-white">
                    <div className="container mx-auto px-4 py-3 flex items-center justify-between">
                        <div className="flex items-center gap-2">
                            <svg className="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L4.082 16.5c-.77.833.192 2.5 1.732 2.5z" />
                            </svg>
                            <span className="text-sm font-medium">
                                Application settings are incomplete. Access Settings via the gear icon to configure{'\u2014'}you will need the admin password.
                            </span>
                        </div>
                    </div>
                </div>
            )}
            <LoginPage hasTopBanner={settingsStatus && (!settingsStatus.isPasswordSet || !settingsStatus.isConfigured)} />
        </>
    );
};

export default HomePage;