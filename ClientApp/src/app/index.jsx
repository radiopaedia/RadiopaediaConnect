import { useState, useEffect } from 'react';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';

const HomePage = () => {
    const [user, setUser] = useState(null);
    const [loading, setLoading] = useState(true);

    // Capture query params for auto-search
    useEffect(() => {
        const params = new URLSearchParams(window.location.search);
        const accession = params.get('accession');
        // Check for 'node.name' or 'node'
        const nodeName = params.get('node.name') || params.get('node');

        if (accession) {
            localStorage.setItem('rp_auto_accession', accession);
        }
        if (nodeName) {
            localStorage.setItem('rp_auto_node', nodeName);
        }
    }, []);

    // check authentication status on mount
    useEffect(() => {
        fetch('/api/auth/me')
            .then((res) => {
                if (res.ok) return res.json();
                return null;
            })
            .then((data) => {
                // map 'username' to 'name'
                if (data) {
                    setUser({ name: data.username, ...data });
                } else {
                    setUser(null);
                }
            })
            .catch((err) => {
                console.error("Auth check failed", err);
                setUser(null);
            })
            .finally(() => setLoading(false));
    }, []);

    const handleLogout = async () => {
        try {
            await fetch('/api/auth/logout', { method: 'POST' });
            setUser(null);
            // Reload the page to reset state
            window.location.reload();
        } catch (error) {
            console.error("Logout failed", error);
        }
    };

    
    if (loading) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-white dark:bg-slate-900">
                <div className="text-lg text-slate-600">Loading...</div>
            </div>
        );
    }

    //Show Dashboard if logged in
    if (user) {
        return <DashboardPage user={user} onLogout={handleLogout} />;
    }

    //Render Login View
    return <LoginPage />;
};

export default HomePage;
