import { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router';

const SetupPage = () => {
    const navigate = useNavigate();
    const [password, setPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const [checkingStatus, setCheckingStatus] = useState(true);
    const inputRef = useRef(null);

    // Redirect if password is already set
    useEffect(() => {
        const checkStatus = async () => {
            try {
                const res = await fetch('/api/settings/status');
                if (res.ok) {
                    const data = await res.json();
                    if (data.isPasswordSet) {
                        navigate('/');
                        return;
                    }
                }
            } catch {
                // Continue to setup if check fails
            } finally {
                setCheckingStatus(false);
            }
        };
        checkStatus();
    }, [navigate]);

    useEffect(() => {
        if (!checkingStatus) {
            inputRef.current?.focus();
        }
    }, [checkingStatus]);

    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');

        if (password.length < 6) {
            setError('Password must be at least 6 characters.');
            return;
        }
        if (password !== confirmPassword) {
            setError('Passwords do not match.');
            return;
        }

        setLoading(true);
        try {
            const res = await fetch('/api/settings/password/setup', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ password }),
            });

            if (res.ok) {
                navigate('/settings');
            } else {
                const data = await res.json();
                setError(data.message || 'Failed to set password.');
            }
        } catch {
            setError('An error occurred. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    if (checkingStatus) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-slate-50 dark:bg-slate-900">
                <div className="text-slate-500">Loading...</div>
            </div>
        );
    }

    return (
        <div className="flex min-h-screen items-center justify-center bg-slate-50 dark:bg-slate-900 px-4">
            <div className="w-full max-w-md">
                <div className="bg-white dark:bg-slate-800 rounded-xl shadow-lg border border-slate-200 dark:border-slate-700 p-8">
                    <div className="text-center mb-6">
                        <div className="mx-auto w-14 h-14 rounded-full bg-indigo-100 dark:bg-indigo-900/50 flex items-center justify-center mb-4">
                            <svg className="w-7 h-7 text-indigo-600 dark:text-indigo-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.066 2.573c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.573 1.066c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.066-2.573c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                            </svg>
                        </div>
                        <h1 className="text-2xl font-bold text-slate-900 dark:text-white">Initial Setup</h1>
                        <p className="text-sm text-slate-500 dark:text-slate-400 mt-2">
                            Welcome to RadiopaediaConnect. Create an admin password to protect the application settings.
                        </p>
                    </div>

                    <form onSubmit={handleSubmit} className="space-y-4">
                        <div>
                            <label className="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-1">Admin Password</label>
                            <input
                                ref={inputRef}
                                type="password"
                                value={password}
                                onChange={(e) => setPassword(e.target.value)}
                                className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 dark:text-white"
                                placeholder="Minimum 6 characters"
                                disabled={loading}
                            />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-1">Confirm Password</label>
                            <input
                                type="password"
                                value={confirmPassword}
                                onChange={(e) => setConfirmPassword(e.target.value)}
                                className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 dark:text-white"
                                placeholder="Re-enter password"
                                disabled={loading}
                            />
                        </div>

                        {error && (
                            <p className="text-sm text-red-600 dark:text-red-400">{error}</p>
                        )}

                        <button
                            type="submit"
                            disabled={loading}
                            className="w-full py-2.5 bg-indigo-600 text-white text-sm font-medium rounded-md shadow-sm hover:bg-indigo-700 transition-colors disabled:opacity-50"
                        >
                            {loading ? 'Setting up...' : 'Set Admin Password & Continue'}
                        </button>
                    </form>

                    <p className="mt-4 text-xs text-center text-slate-400 dark:text-slate-500">
                        This password protects the application settings. Store it securely{'\u2014'}you{'\u2019'}ll need it to make configuration changes.
                    </p>
                </div>
            </div>
        </div>
    );
};

export default SetupPage;