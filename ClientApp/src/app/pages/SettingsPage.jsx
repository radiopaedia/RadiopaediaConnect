import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router';
import { getAdminPassword, clearAdminPassword } from './AdminPasswordModal';

const TABS = [
    { id: 'scp', label: 'DICOM SCP' },
    { id: 'nodes', label: 'Remote Nodes' },
    { id: 'radiopaedia', label: 'Radiopaedia' },
    { id: 'notifications', label: 'Notifications' },
    { id: 'password', label: 'Change Password' },
];

const SettingsPage = () => {
    const navigate = useNavigate();
    const [adminPassword, setAdminPasswordState] = useState('');
    const [authorized, setAuthorized] = useState(false);
    const [activeTab, setActiveTab] = useState('scp');
    const [saving, setSaving] = useState(false);
    const [saveMessage, setSaveMessage] = useState(null);

    // Local settings state
    const [localSettings, setLocalSettings] = useState({
        storageScpAeTitle: 'RCONNECT_SCP',
        maxConcurrentDownloads: 5,
        radiopaediaClientId: '',
        radiopaediaClientSecret: '',
        smtpHost: '',
        smtpPort: null,
        smtpUsername: '',
        smtpPassword: '',
        smtpFromAddress: '',
        notificationRecipients: '',
    });

    // Remote nodes state
    const [nodes, setNodes] = useState([]);
    const [editingNode, setEditingNode] = useState(null);
    const [echoResults, setEchoResults] = useState({});
    const [echoLoading, setEchoLoading] = useState({});

    // Password change state
    const [passwordForm, setPasswordForm] = useState({ currentPassword: '', newPassword: '', confirmPassword: '' });
    const [passwordError, setPasswordError] = useState('');

    const apiHeaders = useCallback(() => ({
        'Content-Type': 'application/json',
        'X-Admin-Password': adminPassword,
    }), [adminPassword]);

    // On mount, read password from sessionStorage and verify
    useEffect(() => {
        const stored = getAdminPassword();
        if (!stored) {
            navigate('/');
            return;
        }

        const verify = async () => {
            try {
                const res = await fetch('/api/settings/password/verify', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ password: stored }),
                });

                if (res.ok) {
                    setAdminPasswordState(stored);
                    setAuthorized(true);
                } else {
                    clearAdminPassword();
                    navigate('/');
                }
            } catch {
                clearAdminPassword();
                navigate('/');
            }
        };

        verify();
    }, [navigate]);

    // Load settings once authorized
    useEffect(() => {
        if (!authorized || !adminPassword) return;

        const loadSettings = async () => {
            try {
                const [localRes, nodesRes] = await Promise.all([
                    fetch('/api/settings/local', { headers: apiHeaders() }),
                    fetch('/api/settings/nodes', { headers: apiHeaders() }),
                ]);

                if (localRes.status === 401 || nodesRes.status === 401) {
                    clearAdminPassword();
                    navigate('/');
                    return;
                }

                if (localRes.ok) {
                    const data = await localRes.json();
                    setLocalSettings(data);
                }
                if (nodesRes.ok) {
                    const data = await nodesRes.json();
                    setNodes(data);
                }
            } catch (err) {
                console.error('Failed to load settings', err);
            }
        };

        loadSettings();
    }, [authorized, adminPassword, apiHeaders, navigate]);

    const showMessage = (text, isError = false) => {
        setSaveMessage({ text, isError });
        setTimeout(() => setSaveMessage(null), 4000);
    };

    const handleBackToApp = () => {
        clearAdminPassword();
        navigate('/');
    };

    const handleSaveLocal = async () => {
        setSaving(true);
        try {
            const res = await fetch('/api/settings/local', {
                method: 'PUT',
                headers: apiHeaders(),
                body: JSON.stringify(localSettings),
            });

            if (res.ok) {
                const data = await res.json();
                showMessage('Settings saved successfully.');
                if (data.scpRestartRequired) {
                    await fetch('/api/settings/scp/restart', {
                        method: 'POST',
                        headers: apiHeaders(),
                    });
                    showMessage('Settings saved. DICOM SCP restarted with new AE Title.');
                }
            } else {
                showMessage('Failed to save settings.', true);
            }
        } catch {
            showMessage('Error saving settings.', true);
        } finally {
            setSaving(false);
        }
    };

    const emptyNode = { name: '', aeTitle: '', host: '', port: 104, callingAe: 'RCONNECT_SCU', sortOrder: nodes.length };

    const handleSaveNode = async () => {
        if (!editingNode) return;
        const { name, aeTitle, host } = editingNode;
        if (!name.trim() || !aeTitle.trim() || !host.trim()) {
            showMessage('Name, AE Title, and Host are required.', true);
            return;
        }

        setSaving(true);
        try {
            const isNew = !editingNode.id;
            const url = isNew ? '/api/settings/nodes' : `/api/settings/nodes/${editingNode.id}`;
            const method = isNew ? 'POST' : 'PUT';

            const res = await fetch(url, {
                method,
                headers: apiHeaders(),
                body: JSON.stringify(editingNode),
            });

            if (res.ok) {
                const nodesRes = await fetch('/api/settings/nodes', { headers: apiHeaders() });
                if (nodesRes.ok) setNodes(await nodesRes.json());
                setEditingNode(null);
                showMessage(isNew ? 'Node added.' : 'Node updated.');
            } else {
                showMessage('Failed to save node.', true);
            }
        } catch {
            showMessage('Error saving node.', true);
        } finally {
            setSaving(false);
        }
    };

    const handleDeleteNode = async (id) => {
        if (!window.confirm('Delete this remote node?')) return;

        try {
            const res = await fetch(`/api/settings/nodes/${id}`, {
                method: 'DELETE',
                headers: apiHeaders(),
            });

            if (res.ok) {
                setNodes((prev) => prev.filter((n) => n.id !== id));
                showMessage('Node deleted.');
            }
        } catch {
            showMessage('Error deleting node.', true);
        }
    };

    const handleEchoNode = async (node) => {
        const key = node.id || 'new';
        setEchoLoading((prev) => ({ ...prev, [key]: true }));
        setEchoResults((prev) => ({ ...prev, [key]: null }));

        try {
            const res = await fetch('/api/settings/nodes/echo', {
                method: 'POST',
                headers: apiHeaders(),
                body: JSON.stringify({
                    host: node.host,
                    port: node.port,
                    aeTitle: node.aeTitle,
                    callingAe: node.callingAe || localSettings.storageScpAeTitle,
                }),
            });

            if (res.ok) {
                const data = await res.json();
                setEchoResults((prev) => ({ ...prev, [key]: data }));
            }
        } catch {
            setEchoResults((prev) => ({ ...prev, [key]: { success: false, message: 'Request failed.' } }));
        } finally {
            setEchoLoading((prev) => ({ ...prev, [key]: false }));
        }
    };

    const handleChangePassword = async () => {
        setPasswordError('');
        if (passwordForm.newPassword.length < 6) {
            setPasswordError('New password must be at least 6 characters.');
            return;
        }
        if (passwordForm.newPassword !== passwordForm.confirmPassword) {
            setPasswordError('Passwords do not match.');
            return;
        }

        setSaving(true);
        try {
            const res = await fetch('/api/settings/password/change', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    currentPassword: passwordForm.currentPassword,
                    newPassword: passwordForm.newPassword,
                }),
            });

            if (res.ok) {
                showMessage('Password changed. You will need to re-enter it next time.');
                setPasswordForm({ currentPassword: '', newPassword: '', confirmPassword: '' });
                clearAdminPassword();
            } else {
                const data = await res.json();
                setPasswordError(data.message || 'Failed to change password.');
            }
        } catch {
            setPasswordError('Error changing password.');
        } finally {
            setSaving(false);
        }
    };

    if (!authorized) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-slate-50 dark:bg-slate-900">
                <div className="text-slate-500">Verifying access...</div>
            </div>
        );
    }

    return (
        <div className="min-h-screen bg-slate-50 dark:bg-slate-900 text-slate-900 dark:text-slate-100">
            {/* Header */}
            <header className="flex items-center justify-between h-16 px-4 bg-white dark:bg-slate-800 shadow-sm">
                <div className="flex items-center">
                    <button
                        onClick={handleBackToApp}
                        className="mr-4 p-2 rounded-md hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
                        title="Back to app"
                    >
                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10 19l-7-7m0 0l7-7m-7 7h18" />
                        </svg>
                    </button>
                    <svg className="w-6 h-6 text-indigo-600 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.066 2.573c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.573 1.066c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.066-2.573c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                    </svg>
                    <h1 className="text-xl font-bold">Settings</h1>
                </div>
            </header>

            {/* Toast message */}
            {saveMessage && (
                <div className={`fixed top-4 right-4 z-50 px-4 py-3 rounded-lg shadow-lg text-sm font-medium transition-all ${saveMessage.isError
                    ? 'bg-red-100 text-red-800 dark:bg-red-900/80 dark:text-red-200'
                    : 'bg-green-100 text-green-800 dark:bg-green-900/80 dark:text-green-200'
                    }`}>
                    {saveMessage.text}
                </div>
            )}

            <div className="container mx-auto max-w-4xl p-4">
                {/* Tabs */}
                <div className="flex space-x-1 bg-white dark:bg-slate-800 rounded-lg p-1 shadow-sm mb-6 overflow-x-auto">
                    {TABS.map((tab) => (
                        <button
                            key={tab.id}
                            onClick={() => setActiveTab(tab.id)}
                            className={`flex-shrink-0 px-4 py-2 text-sm font-medium rounded-md transition-colors ${activeTab === tab.id
                                ? 'bg-indigo-600 text-white shadow-sm'
                                : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white hover:bg-slate-100 dark:hover:bg-slate-700'
                                }`}
                        >
                            {tab.label}
                        </button>
                    ))}
                </div>

                {/* Tab Content */}
                <div className="bg-white dark:bg-slate-800 rounded-lg shadow-sm border border-slate-200 dark:border-slate-700 p-6">

                    {activeTab === 'scp' && (
                        <div className="space-y-6">
                            <div>
                                <h2 className="text-lg font-semibold mb-1">DICOM Storage SCP</h2>
                                <p className="text-sm text-slate-500 dark:text-slate-400 mb-4">
                                    Configure the local DICOM receiver. Port is fixed at 104 internally{'\u2014'}map it externally via Docker{'\u2019'}s <code className="text-xs bg-slate-100 dark:bg-slate-700 px-1 py-0.5 rounded">-p</code> flag.
                                </p>
                            </div>

                            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium mb-1">AE Title</label>
                                    <input
                                        type="text"
                                        value={localSettings.storageScpAeTitle}
                                        onChange={(e) => setLocalSettings((s) => ({ ...s, storageScpAeTitle: e.target.value.toUpperCase() }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                        maxLength={16}
                                        placeholder="RCONNECT_SCP"
                                    />
                                    <p className="mt-1 text-xs text-slate-400">Max 16 characters, uppercase. Changing this restarts the SCP.</p>
                                </div>
                                <div>
                                    <label className="block text-sm font-medium mb-1">Port</label>
                                    <input
                                        type="text"
                                        value="104"
                                        disabled
                                        className="w-full rounded-md border border-slate-200 dark:border-slate-700 bg-slate-100 dark:bg-slate-800 px-3 py-2 text-sm text-slate-500"
                                    />
                                    <p className="mt-1 text-xs text-slate-400">Fixed. Use Docker port mapping (e.g. -p 11112:104).</p>
                                </div>
                            </div>

                            <div className="max-w-xs">
                                <label className="block text-sm font-medium mb-1">Max Concurrent Downloads</label>
                                <input
                                    type="number"
                                    min={1}
                                    max={20}
                                    value={localSettings.maxConcurrentDownloads}
                                    onChange={(e) => setLocalSettings((s) => ({ ...s, maxConcurrentDownloads: parseInt(e.target.value, 10) || 1 }))}
                                    className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                />
                            </div>

                            <div className="pt-4 border-t border-slate-200 dark:border-slate-700">
                                <button
                                    onClick={handleSaveLocal}
                                    disabled={saving}
                                    className="px-6 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md shadow-sm hover:bg-indigo-700 transition-colors disabled:opacity-50"
                                >
                                    {saving ? 'Saving...' : 'Save SCP Settings'}
                                </button>
                            </div>
                        </div>
                    )}

                    {activeTab === 'nodes' && (
                        <div className="space-y-6">
                            <div className="flex items-center justify-between">
                                <div>
                                    <h2 className="text-lg font-semibold">Remote DICOM Nodes</h2>
                                    <p className="text-sm text-slate-500 dark:text-slate-400">PACS servers this application can query and retrieve from.</p>
                                </div>
                                <button
                                    onClick={() => setEditingNode({ ...emptyNode })}
                                    className="px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md shadow-sm hover:bg-indigo-700 transition-colors"
                                >
                                    + Add Node
                                </button>
                            </div>

                            {nodes.length === 0 && !editingNode && (
                                <div className="text-center py-8 text-slate-400">
                                    <svg className="w-12 h-12 mx-auto mb-3 opacity-50" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d="M5 12h14M5 12a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v4a2 2 0 01-2 2M5 12a2 2 0 00-2 2v4a2 2 0 002 2h14a2 2 0 002-2v-4a2 2 0 00-2-2" />
                                    </svg>
                                    <p>No remote nodes configured.</p>
                                </div>
                            )}

                            {nodes.map((node) => {
                                const echoKey = node.id;
                                const echoResult = echoResults[echoKey];
                                const isEchoing = echoLoading[echoKey];

                                return (
                                    <div key={node.id} className="border border-slate-200 dark:border-slate-700 rounded-lg p-4">
                                        <div className="flex items-start justify-between">
                                            <div>
                                                <h3 className="font-semibold text-slate-900 dark:text-white">{node.name}</h3>
                                                <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">
                                                    {node.aeTitle}@{node.host}:{node.port}
                                                    <span className="ml-2 text-slate-400">| Calling AE: {node.callingAe}</span>
                                                </p>
                                            </div>
                                            <div className="flex gap-2">
                                                <button
                                                    onClick={() => handleEchoNode(node)}
                                                    disabled={isEchoing}
                                                    className="px-3 py-1.5 text-xs font-medium rounded-md border border-slate-300 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors disabled:opacity-50"
                                                    title="DICOM C-ECHO (ping)"
                                                >
                                                    {isEchoing ? 'Pinging...' : 'C-ECHO'}
                                                </button>
                                                <button
                                                    onClick={() => setEditingNode({ ...node })}
                                                    className="px-3 py-1.5 text-xs font-medium text-indigo-600 dark:text-indigo-400 rounded-md border border-indigo-300 dark:border-indigo-600 hover:bg-indigo-50 dark:hover:bg-indigo-900/20 transition-colors"
                                                >
                                                    Edit
                                                </button>
                                                <button
                                                    onClick={() => handleDeleteNode(node.id)}
                                                    className="px-3 py-1.5 text-xs font-medium text-red-600 dark:text-red-400 rounded-md border border-red-300 dark:border-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 transition-colors"
                                                >
                                                    Delete
                                                </button>
                                            </div>
                                        </div>

                                        {echoResult && (
                                            <div className={`mt-3 px-3 py-2 rounded text-sm ${echoResult.success
                                                ? 'bg-green-50 dark:bg-green-900/20 text-green-700 dark:text-green-400'
                                                : 'bg-red-50 dark:bg-red-900/20 text-red-700 dark:text-red-400'
                                                }`}>
                                                {echoResult.success ? '\u2713' : '\u2717'} {echoResult.message}
                                            </div>
                                        )}
                                    </div>
                                );
                            })}

                            {/* Edit / Add Form */}
                            {editingNode && (
                                <div className="border-2 border-indigo-300 dark:border-indigo-600 rounded-lg p-4 bg-indigo-50/50 dark:bg-indigo-900/10">
                                    <h3 className="font-semibold mb-4">{editingNode.id ? 'Edit Node' : 'Add New Node'}</h3>
                                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                                        <div>
                                            <label className="block text-sm font-medium mb-1">Display Name</label>
                                            <input
                                                type="text"
                                                value={editingNode.name}
                                                onChange={(e) => setEditingNode((n) => ({ ...n, name: e.target.value }))}
                                                className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                                placeholder="e.g. Main PACS"
                                            />
                                        </div>
                                        <div>
                                            <label className="block text-sm font-medium mb-1">AE Title</label>
                                            <input
                                                type="text"
                                                value={editingNode.aeTitle}
                                                onChange={(e) => setEditingNode((n) => ({ ...n, aeTitle: e.target.value.toUpperCase() }))}
                                                className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                                maxLength={16}
                                                placeholder="PACS_AE"
                                            />
                                        </div>
                                        <div>
                                            <label className="block text-sm font-medium mb-1">Host / IP</label>
                                            <input
                                                type="text"
                                                value={editingNode.host}
                                                onChange={(e) => setEditingNode((n) => ({ ...n, host: e.target.value }))}
                                                className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                                placeholder="10.0.0.1"
                                            />
                                        </div>
                                        <div>
                                            <label className="block text-sm font-medium mb-1">Port</label>
                                            <input
                                                type="number"
                                                value={editingNode.port}
                                                onChange={(e) => setEditingNode((n) => ({ ...n, port: parseInt(e.target.value, 10) || 104 }))}
                                                className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                            />
                                        </div>
                                        <div className="sm:col-span-2">
                                            <label className="block text-sm font-medium mb-1">Calling AE Title</label>
                                            <input
                                                type="text"
                                                value={editingNode.callingAe}
                                                onChange={(e) => setEditingNode((n) => ({ ...n, callingAe: e.target.value.toUpperCase() }))}
                                                className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                                maxLength={16}
                                                placeholder="RCONNECT_SCU"
                                            />
                                            <p className="mt-1 text-xs text-slate-400">The AE Title this app uses when connecting to this node. Must be registered on the remote PACS.</p>
                                        </div>
                                    </div>

                                    {echoResults.new && !editingNode.id && (
                                        <div className={`mt-3 px-3 py-2 rounded text-sm ${echoResults.new.success
                                            ? 'bg-green-50 dark:bg-green-900/20 text-green-700 dark:text-green-400'
                                            : 'bg-red-50 dark:bg-red-900/20 text-red-700 dark:text-red-400'
                                            }`}>
                                            {echoResults.new.success ? '\u2713' : '\u2717'} {echoResults.new.message}
                                        </div>
                                    )}

                                    <div className="mt-4 flex gap-2">
                                        <button
                                            onClick={() => handleEchoNode(editingNode)}
                                            disabled={!editingNode.host || !editingNode.aeTitle || echoLoading[editingNode.id || 'new']}
                                            className="px-4 py-2 text-sm font-medium rounded-md border border-slate-300 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors disabled:opacity-50"
                                        >
                                            {echoLoading[editingNode.id || 'new'] ? 'Pinging...' : 'Test C-ECHO'}
                                        </button>
                                        <button
                                            onClick={handleSaveNode}
                                            disabled={saving}
                                            className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md shadow-sm hover:bg-indigo-700 transition-colors disabled:opacity-50"
                                        >
                                            {saving ? 'Saving...' : 'Save Node'}
                                        </button>
                                        <button
                                            onClick={() => { setEditingNode(null); setEchoResults((prev) => ({ ...prev, new: undefined })); }}
                                            className="px-4 py-2 text-sm font-medium text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700 rounded-md transition-colors"
                                        >
                                            Cancel
                                        </button>
                                    </div>
                                </div>
                            )}
                        </div>
                    )}

                    {/* ── Radiopaedia Tab ─────────────────────────── */}
                    {activeTab === 'radiopaedia' && (
                        <div className="space-y-6">
                            <div>
                                <h2 className="text-lg font-semibold mb-1">Radiopaedia OAuth Credentials</h2>
                                <p className="text-sm text-slate-500 dark:text-slate-400 mb-1">
                                    These credentials are required for user authentication and case submission. Obtain them from Radiopaedia.
                                </p>
                                <a
                                    href="https://radiopaedia.org/oauth/applications"
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="inline-flex items-center gap-1.5 text-sm text-indigo-600 dark:text-indigo-400 hover:underline"
                                >
                                    <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                                    </svg>
                                    Manage OAuth Applications on Radiopaedia
                                </a>
                            </div>

                            <div className="space-y-4">
                                <div>
                                    <label className="block text-sm font-medium mb-1">Client ID</label>
                                    <input
                                        type="text"
                                        value={localSettings.radiopaediaClientId}
                                        onChange={(e) => setLocalSettings((s) => ({ ...s, radiopaediaClientId: e.target.value }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm font-mono shadow-sm"
                                        placeholder="Enter Client ID"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium mb-1">Client Secret</label>
                                    <input
                                        type="password"
                                        value={localSettings.radiopaediaClientSecret}
                                        onChange={(e) => setLocalSettings((s) => ({ ...s, radiopaediaClientSecret: e.target.value }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm font-mono shadow-sm"
                                        placeholder="Enter Client Secret"
                                    />
                                </div>
                            </div>

                            <div className="pt-4 border-t border-slate-200 dark:border-slate-700">
                                <button
                                    onClick={handleSaveLocal}
                                    disabled={saving}
                                    className="px-6 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md shadow-sm hover:bg-indigo-700 transition-colors disabled:opacity-50"
                                >
                                    {saving ? 'Saving...' : 'Save Radiopaedia Settings'}
                                </button>
                            </div>
                        </div>
                    )}

                    {/* ── Notifications Tab ───────────────────────── */}
                    {activeTab === 'notifications' && (
                        <div className="space-y-6">
                            <div>
                                <h2 className="text-lg font-semibold mb-1">Email Notifications</h2>
                                <div className="mt-2 flex items-center gap-2 px-3 py-2 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-700 rounded-md">
                                    <svg className="w-5 h-5 text-amber-500 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L4.082 16.5c-.77.833.192 2.5 1.732 2.5z" />
                                    </svg>
                                    <p className="text-sm text-amber-800 dark:text-amber-200">
                                        This feature is coming soon. Configure your SMTP settings now and they will be used when notifications are enabled.
                                    </p>
                                </div>
                            </div>

                            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 opacity-80">
                                <div>
                                    <label className="block text-sm font-medium mb-1">SMTP Host</label>
                                    <input
                                        type="text"
                                        value={localSettings.smtpHost}
                                        onChange={(e) => setLocalSettings((s) => ({ ...s, smtpHost: e.target.value }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                        placeholder="smtp.example.com"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium mb-1">SMTP Port</label>
                                    <input
                                        type="number"
                                        value={localSettings.smtpPort || ''}
                                        onChange={(e) => setLocalSettings((s) => ({ ...s, smtpPort: parseInt(e.target.value, 10) || null }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                        placeholder="587"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium mb-1">Username</label>
                                    <input
                                        type="text"
                                        value={localSettings.smtpUsername}
                                        onChange={(e) => setLocalSettings((s) => ({ ...s, smtpUsername: e.target.value }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium mb-1">Password</label>
                                    <input
                                        type="password"
                                        value={localSettings.smtpPassword}
                                        onChange={(e) => setLocalSettings((s) => ({ ...s, smtpPassword: e.target.value }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium mb-1">From Address</label>
                                    <input
                                        type="email"
                                        value={localSettings.smtpFromAddress}
                                        onChange={(e) => setLocalSettings((s) => ({ ...s, smtpFromAddress: e.target.value }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                        placeholder="noreply@example.com"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium mb-1">Notification Recipients</label>
                                    <input
                                        type="text"
                                        value={localSettings.notificationRecipients}
                                        onChange={(e) => setLocalSettings((s) => ({ ...s, notificationRecipients: e.target.value }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                        placeholder="admin@example.com, ops@example.com"
                                    />
                                    <p className="mt-1 text-xs text-slate-400">Comma-separated email addresses.</p>
                                </div>
                            </div>

                            <div className="pt-4 border-t border-slate-200 dark:border-slate-700">
                                <button
                                    onClick={handleSaveLocal}
                                    disabled={saving}
                                    className="px-6 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md shadow-sm hover:bg-indigo-700 transition-colors disabled:opacity-50"
                                >
                                    {saving ? 'Saving...' : 'Save Notification Settings'}
                                </button>
                            </div>
                        </div>
                    )}

                    {/* ── Change Password Tab ────────────────────── */}
                    {activeTab === 'password' && (
                        <div className="space-y-6 max-w-md">
                            <div>
                                <h2 className="text-lg font-semibold mb-1">Change Admin Password</h2>
                                <p className="text-sm text-slate-500 dark:text-slate-400 mb-4">
                                    Update the password used to access these settings.
                                </p>
                            </div>

                            <div className="space-y-4">
                                <div>
                                    <label className="block text-sm font-medium mb-1">Current Password</label>
                                    <input
                                        type="password"
                                        value={passwordForm.currentPassword}
                                        onChange={(e) => setPasswordForm((f) => ({ ...f, currentPassword: e.target.value }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium mb-1">New Password</label>
                                    <input
                                        type="password"
                                        value={passwordForm.newPassword}
                                        onChange={(e) => setPasswordForm((f) => ({ ...f, newPassword: e.target.value }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium mb-1">Confirm New Password</label>
                                    <input
                                        type="password"
                                        value={passwordForm.confirmPassword}
                                        onChange={(e) => setPasswordForm((f) => ({ ...f, confirmPassword: e.target.value }))}
                                        className="w-full rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-3 py-2 text-sm shadow-sm"
                                    />
                                </div>
                            </div>

                            {passwordError && (
                                <p className="text-sm text-red-600 dark:text-red-400">{passwordError}</p>
                            )}

                            <button
                                onClick={handleChangePassword}
                                disabled={saving || !passwordForm.currentPassword || !passwordForm.newPassword}
                                className="px-6 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md shadow-sm hover:bg-indigo-700 transition-colors disabled:opacity-50"
                            >
                                {saving ? 'Changing...' : 'Change Password'}
                            </button>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
};

export default SettingsPage;