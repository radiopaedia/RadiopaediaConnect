import { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router';


const RecoveryPanel = ({ onCancel }) => {
    const navigate = useNavigate();
    const [appSecret, setAppSecret] = useState('');
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const inputRef = useRef(null);

    useEffect(() => {
        const timer = setTimeout(() => inputRef.current?.focus(), 50);
        return () => clearTimeout(timer);
    }, []);

    const handleRecover = async (e) => {
        e.preventDefault();
        if (!appSecret.trim()) {
            setError('Please enter the Radiopaedia App Secret.');
            return;
        }

        setLoading(true);
        setError('');

        try {
            const res = await fetch('/api/settings/password/recover', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ appSecret }),
            });

            if (res.ok) {
                // Password has been cleared server-side; invalidate any active session
                // and redirect to the first-run setup page to create a new password.
                await fetch('/api/settings/logout', { method: 'POST' });
                navigate('/setup');
            } else {
                const data = await res.json().catch(() => ({}));
                setError(data.message || 'Incorrect app secret.');
                setAppSecret('');
                inputRef.current?.focus();
            }
        } catch {
            setError('Failed to contact the server. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="mt-4 rounded-md border border-amber-200 dark:border-amber-700/50 bg-amber-50 dark:bg-amber-900/20 p-4">
            <p className="text-xs text-amber-800 dark:text-amber-300 mb-3">
                Enter the <strong>Radiopaedia App Secret</strong> configured in your application settings.
                If correct, the admin password will be reset and you can create a new one.
            </p>

            <form onSubmit={handleRecover}>
                <input
                    ref={inputRef}
                    type="password"
                    value={appSecret}
                    onChange={(e) => setAppSecret(e.target.value)}
                    placeholder="Radiopaedia App Secret"
                    className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm focus:border-amber-500 focus:ring-1 focus:ring-amber-500 dark:text-white"
                    disabled={loading}
                />

                {error && (
                    <p className="mt-2 text-sm text-red-600 dark:text-red-400">{error}</p>
                )}

                <div className="mt-3 flex justify-end gap-2">
                    <button
                        type="button"
                        onClick={onCancel}
                        className="px-3 py-1.5 text-xs font-medium text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700 rounded-md transition-colors"
                        disabled={loading}
                    >
                        Cancel
                    </button>
                    <button
                        type="submit"
                        className="px-3 py-1.5 text-xs font-medium text-white bg-amber-600 hover:bg-amber-700 rounded-md shadow-sm transition-colors disabled:opacity-50"
                        disabled={loading}
                    >
                        {loading ? 'Verifying\u2026' : 'Reset Password'}
                    </button>
                </div>
            </form>
        </div>
    );
};

const AdminPasswordModal = ({ isOpen, onClose, onSuccess }) => {
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const [showRecovery, setShowRecovery] = useState(false);
    const inputRef = useRef(null);

    useEffect(() => {
        if (isOpen) {
            setPassword('');
            setError('');
            setShowRecovery(false);
            const timer = setTimeout(() => inputRef.current?.focus(), 100);
            return () => clearTimeout(timer);
        }
    }, [isOpen]);

    const handleSubmit = async (e) => {
        e.preventDefault();
        if (!password.trim()) {
            setError('Please enter the admin password.');
            return;
        }

        setLoading(true);
        setError('');

        try {
            const res = await fetch('/api/settings/password/verify', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ password }),
            });

            if (res.ok) {
                onSuccess();
            } else {
                setError('Incorrect password.');
                setPassword('');
                inputRef.current?.focus();
            }
        } catch {
            setError('Failed to verify password. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
            <div
                className="absolute inset-0 bg-black/50 backdrop-blur-sm"
                onClick={onClose}
            />
            <div className="relative bg-white dark:bg-slate-800 rounded-lg shadow-xl w-full max-w-sm mx-4 p-6">
                <div className="flex items-center mb-4">
                    <div className="flex items-center justify-center w-10 h-10 rounded-full bg-indigo-100 dark:bg-indigo-900/50 mr-3">
                        <svg className="w-5 h-5 text-indigo-600 dark:text-indigo-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                        </svg>
                    </div>
                    <div>
                        <h3 className="text-lg font-semibold text-slate-900 dark:text-white">Admin Access</h3>
                        <p className="text-sm text-slate-500 dark:text-slate-400">Enter the admin password to continue</p>
                    </div>
                </div>

                <form onSubmit={handleSubmit}>
                    <input
                        ref={inputRef}
                        type="password"
                        value={password}
                        onChange={(e) => setPassword(e.target.value)}
                        placeholder="Admin password"
                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 dark:text-white"
                        disabled={loading}
                    />

                    {error && (
                        <p className="mt-2 text-sm text-red-600 dark:text-red-400">{error}</p>
                    )}

                    <div className="mt-4 flex justify-between items-center">
                        <button
                            type="button"
                            onClick={() => { setError(''); setShowRecovery((v) => !v); }}
                            className="text-xs text-slate-400 dark:text-slate-500 hover:text-indigo-500 dark:hover:text-indigo-400 transition-colors"
                            disabled={loading}
                        >
                            {showRecovery ? 'Hide recovery' : 'Forgot password?'}
                        </button>

                        <div className="flex gap-2">
                            <button
                                type="button"
                                onClick={onClose}
                                className="px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 rounded-md transition-colors"
                                disabled={loading}
                            >
                                Cancel
                            </button>
                            <button
                                type="submit"
                                className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-md shadow-sm transition-colors disabled:opacity-50"
                                disabled={loading}
                            >
                                {loading ? 'Verifying\u2026' : 'Unlock'}
                            </button>
                        </div>
                    </div>
                </form>

                {showRecovery && (
                    <RecoveryPanel onCancel={() => setShowRecovery(false)} />
                )}
            </div>
        </div>
    );
};

export default AdminPasswordModal;