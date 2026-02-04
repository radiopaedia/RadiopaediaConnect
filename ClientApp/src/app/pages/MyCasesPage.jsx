import { useState, useEffect, useMemo } from 'react';
import { useNavigate } from 'react-router';
import MainLayout from './MainLayout';
import LoginPage from './LoginPage';
import CaseDetailDrawer from './CaseDetailDrawer';

// Status display configuration
const STATUS_CONFIG = {
    'Queued': {
        color: 'bg-yellow-100 text-yellow-800 border-yellow-200',
        icon: (
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
        ),
        label: 'Queued'
    },
    'Processing': {
        color: 'bg-blue-100 text-blue-800 border-blue-200',
        icon: (
            <svg className="w-4 h-4 animate-spin" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
            </svg>
        ),
        label: 'Processing'
    },
    'Completed': {
        color: 'bg-green-100 text-green-800 border-green-200',
        icon: (
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7" />
            </svg>
        ),
        label: 'Completed'
    },
    'Failed': {
        color: 'bg-red-100 text-red-800 border-red-200',
        icon: (
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
        ),
        label: 'Failed'
    }
};

const MyCasesPage = () => {
    const navigate = useNavigate();
    const [user, setUser] = useState(null);
    const [authLoading, setAuthLoading] = useState(true);
    const [cases, setCases] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);

    // Filter state
    const [searchQuery, setSearchQuery] = useState('');
    const [statusFilter, setStatusFilter] = useState(null);

    // Drawer state
    const [drawerOpen, setDrawerOpen] = useState(false);
    const [selectedCaseId, setSelectedCaseId] = useState(null);

    // Check authentication on mount
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
            .catch(() => setUser(null))
            .finally(() => setAuthLoading(false));
    }, []);

    useEffect(() => {
        if (user) {
            fetchCases();
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [user]);

    // Compute counts
    const completedCount = cases.filter(c => c.status === 'Completed').length;
    const pendingCount = cases.filter(c => c.status === 'Queued' || c.status === 'Processing').length;
    const failedCount = cases.filter(c => c.status === 'Failed').length;

    // Filter cases based on search and status
    const filteredCases = useMemo(() => {
        return cases.filter(c => {
            if (statusFilter === 'Completed' && c.status !== 'Completed') return false;
            if (statusFilter === 'Pending' && c.status !== 'Queued' && c.status !== 'Processing') return false;
            if (statusFilter === 'Failed' && c.status !== 'Failed') return false;

            if (searchQuery.trim()) {
                const query = searchQuery.toLowerCase();
                const title = (c.title || '').toLowerCase();
                const patientName = (c.patientName || '').toLowerCase();
                const patientId = (c.patientId || '').toLowerCase();
                return title.includes(query) || patientName.includes(query) || patientId.includes(query);
            }

            return true;
        });
    }, [cases, statusFilter, searchQuery]);

    const handleLogout = async () => {
        try {
            await fetch('/api/auth/logout', { method: 'POST' });
            setUser(null);
            window.location.href = '/';
        } catch (err) {
            console.error("Logout failed", err);
        }
    };

    const fetchCases = async () => {
        setLoading(true);
        setError(null);
        try {
            const response = await fetch('/api/cases/my-cases');
            if (!response.ok) {
                if (response.status === 401) {
                    throw new Error('Please log in to view your cases.');
                }
                throw new Error('Failed to fetch cases');
            }
            const data = await response.json();
            setCases(data);
        } catch (err) {
            setError(err.message);
        } finally {
            setLoading(false);
        }
    };

    const formatDate = (dateString, dateOnly = false) => {
        if (!dateString) return '';
        const options = dateOnly
            ? { year: 'numeric', month: 'short', day: 'numeric' }
            : { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' };
        return new Date(dateString).toLocaleDateString('en-US', options);
    };

    const getStatusConfig = (status) => {
        return STATUS_CONFIG[status] || STATUS_CONFIG['Queued'];
    };

    const getRadiopaediaUrl = (caseId) => {
        return `https://radiopaedia.org/cases/${caseId}`;
    };

    const handleStatusFilterClick = (filter) => {
        if (statusFilter === filter) {
            setStatusFilter(null);
        } else {
            setStatusFilter(filter);
        }
    };

    const clearFilters = () => {
        setStatusFilter(null);
        setSearchQuery('');
    };

    const handleOpenDetail = (caseId) => {
        setSelectedCaseId(caseId);
        setDrawerOpen(true);
    };

    const handleCloseDrawer = () => {
        setDrawerOpen(false);
        setSelectedCaseId(null);
    };

    // Show loading while checking auth
    if (authLoading) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-white dark:bg-slate-900">
                <div className="text-lg text-slate-600">Loading...</div>
            </div>
        );
    }

    // Redirect to login if not authenticated
    if (!user) {
        return <LoginPage />;
    }

    return (
        <MainLayout user={user} onLogout={handleLogout}>
            <div className="p-6 max-w-6xl mx-auto">
                <div className="mb-6 flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4">
                    <div>
                        <h1 className="text-2xl font-bold text-slate-900 dark:text-white">My Cases</h1>
                        <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">
                            View and track your submitted cases
                        </p>
                    </div>
                    <div className="flex items-center gap-3">
                        {/* Search Input */}
                        <div className="relative">
                            <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                            </svg>
                            <input
                                type="text"
                                placeholder="Search title, patient name, or ID..."
                                value={searchQuery}
                                onChange={(e) => setSearchQuery(e.target.value)}
                                className="pl-9 pr-4 py-2 w-48 sm:w-64 border border-slate-300 dark:border-slate-600 rounded-lg text-sm bg-white dark:bg-slate-800 text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                            />
                            {searchQuery && (
                                <button
                                    onClick={() => setSearchQuery('')}
                                    className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600"
                                >
                                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                                    </svg>
                                </button>
                            )}
                        </div>
                        <button
                            onClick={fetchCases}
                            disabled={loading}
                            className="flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors disabled:opacity-50"
                        >
                            <svg className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                            </svg>
                            Refresh
                        </button>
                        <button
                            onClick={() => navigate('/')}
                            className="flex items-center gap-2 px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition-colors"
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4v16m8-8H4" />
                            </svg>
                            Add New Case
                        </button>
                    </div>
                </div>

                {/* Summary Cards - Clickable Filters */}
                {!loading && !error && cases.length > 0 && (
                    <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-6">
                        <button
                            onClick={clearFilters}
                            className={`bg-white dark:bg-slate-800 rounded-lg shadow border px-3 py-2 text-left transition-all hover:shadow-md flex items-center justify-between ${statusFilter === null
                                ? 'border-indigo-500 ring-2 ring-indigo-500/20'
                                : 'border-slate-200 dark:border-slate-700'
                                }`}
                        >
                            <span className="text-xs text-slate-500 dark:text-slate-400">Total</span>
                            <span className="text-lg font-bold text-slate-900 dark:text-white">{cases.length}</span>
                        </button>
                        <button
                            onClick={() => handleStatusFilterClick('Completed')}
                            className={`bg-white dark:bg-slate-800 rounded-lg shadow border px-3 py-2 text-left transition-all hover:shadow-md flex items-center justify-between ${statusFilter === 'Completed'
                                ? 'border-green-500 ring-2 ring-green-500/20'
                                : 'border-green-200 dark:border-green-800'
                                }`}
                        >
                            <span className="flex items-center gap-1.5">
                                <span className="w-2 h-2 rounded-full bg-green-500"></span>
                                <span className="text-xs text-slate-500 dark:text-slate-400">Completed</span>
                            </span>
                            <span className="text-lg font-bold text-green-600 dark:text-green-400">{completedCount}</span>
                        </button>
                        <button
                            onClick={() => handleStatusFilterClick('Pending')}
                            className={`bg-white dark:bg-slate-800 rounded-lg shadow border px-3 py-2 text-left transition-all hover:shadow-md flex items-center justify-between ${statusFilter === 'Pending'
                                ? 'border-yellow-500 ring-2 ring-yellow-500/20'
                                : 'border-yellow-200 dark:border-yellow-800'
                                }`}
                        >
                            <span className="flex items-center gap-1.5">
                                <span className="w-2 h-2 rounded-full bg-yellow-500"></span>
                                <span className="text-xs text-slate-500 dark:text-slate-400">Pending</span>
                            </span>
                            <span className="text-lg font-bold text-yellow-600 dark:text-yellow-400">{pendingCount}</span>
                        </button>
                        <button
                            onClick={() => handleStatusFilterClick('Failed')}
                            className={`bg-white dark:bg-slate-800 rounded-lg shadow border px-3 py-2 text-left transition-all hover:shadow-md flex items-center justify-between ${statusFilter === 'Failed'
                                ? 'border-red-500 ring-2 ring-red-500/20'
                                : 'border-red-200 dark:border-red-800'
                                }`}
                        >
                            <span className="flex items-center gap-1.5">
                                <span className="w-2 h-2 rounded-full bg-red-500"></span>
                                <span className="text-xs text-slate-500 dark:text-slate-400">Failed</span>
                            </span>
                            <span className="text-lg font-bold text-red-600 dark:text-red-400">{failedCount}</span>
                        </button>
                    </div>
                )}

                {/* Active Filters Indicator */}
                {(statusFilter || searchQuery) && (
                    <div className="mb-4 flex items-center gap-2 text-sm text-slate-600 dark:text-slate-400">
                        <span>Showing:</span>
                        {statusFilter && (
                            <span className="inline-flex items-center gap-1 px-2 py-1 bg-slate-100 dark:bg-slate-700 rounded">
                                {statusFilter}
                                <button onClick={() => setStatusFilter(null)} className="hover:text-red-500">
                                    <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                                    </svg>
                                </button>
                            </span>
                        )}
                        {searchQuery && (
                            <span className="inline-flex items-center gap-1 px-2 py-1 bg-slate-100 dark:bg-slate-700 rounded">
                                {'\u201C'}{searchQuery}{'\u201D'}
                                <button onClick={() => setSearchQuery('')} className="hover:text-red-500">
                                    <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                                    </svg>
                                </button>
                            </span>
                        )}
                        <button onClick={clearFilters} className="text-indigo-600 hover:text-indigo-700 ml-2">
                            Clear all
                        </button>
                    </div>
                )}

                {/* Error State */}
                {error && (
                    <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4 mb-6">
                        <div className="flex items-center gap-2 text-red-700 dark:text-red-400">
                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                            </svg>
                            <span>{error}</span>
                        </div>
                    </div>
                )}

                {/* Loading State */}
                {loading && (
                    <div className="bg-white dark:bg-slate-800 rounded-lg shadow p-12 flex flex-col items-center justify-center">
                        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600 mb-4"></div>
                        <p className="text-slate-500">Loading your cases...</p>
                    </div>
                )}

                {/* Empty State */}
                {!loading && !error && cases.length === 0 && (
                    <div className="bg-white dark:bg-slate-800 rounded-lg shadow border border-slate-200 dark:border-slate-700 p-12 text-center">
                        <svg className="w-16 h-16 mx-auto text-slate-300 dark:text-slate-600 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                        </svg>
                        <h3 className="text-lg font-medium text-slate-900 dark:text-white mb-2">No cases yet</h3>
                        <p className="text-slate-500 dark:text-slate-400">
                            Cases you submit will appear here.
                        </p>
                        <a
                            href="/"
                            className="inline-flex items-center gap-2 mt-4 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors"
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4v16m8-8H4" />
                            </svg>
                            Create New Case
                        </a>
                    </div>
                )}

                {/* No Results from Filter */}
                {!loading && !error && cases.length > 0 && filteredCases.length === 0 && (
                    <div className="bg-white dark:bg-slate-800 rounded-lg shadow border border-slate-200 dark:border-slate-700 p-12 text-center">
                        <svg className="w-16 h-16 mx-auto text-slate-300 dark:text-slate-600 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                        </svg>
                        <h3 className="text-lg font-medium text-slate-900 dark:text-white mb-2">No matching cases</h3>
                        <p className="text-slate-500 dark:text-slate-400">
                            Try adjusting your search or filters.
                        </p>
                        <button
                            onClick={clearFilters}
                            className="inline-flex items-center gap-2 mt-4 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors"
                        >
                            Clear Filters
                        </button>
                    </div>
                )}

                {/* Cases List */}
                {!loading && !error && filteredCases.length > 0 && (
                    <div className="bg-white dark:bg-slate-800 rounded-lg shadow border border-slate-200 dark:border-slate-700 overflow-hidden">
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-slate-200 dark:divide-slate-700">
                                <thead className="bg-slate-50 dark:bg-slate-900/50">
                                    <tr>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                                            Patient
                                        </th>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                                            Case
                                        </th>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                                            Status
                                        </th>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                                            Created
                                        </th>
                                        <th className="px-6 py-3 text-right text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                                            Actions
                                        </th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-200 dark:divide-slate-700">
                                    {filteredCases.map((caseItem) => {
                                        const statusConfig = getStatusConfig(caseItem.status);
                                        const hasRadiopaediaLink = caseItem.status === 'Completed' && caseItem.radiopaediaCaseId;

                                        return (
                                            <tr key={caseItem.id} className="hover:bg-slate-50 dark:hover:bg-slate-700/50 transition-colors">
                                                <td className="px-6 py-4">
                                                    <div>
                                                        <div className="text-sm font-medium text-slate-900 dark:text-white">
                                                            {caseItem.patientName || '\u2014'}
                                                        </div>
                                                        <div className="text-xs text-slate-500 dark:text-slate-400 mt-1 space-x-2">
                                                            {caseItem.patientId && (
                                                                <span className="font-mono bg-slate-100 dark:bg-slate-700 px-1.5 py-0.5 rounded">
                                                                    {caseItem.patientId}
                                                                </span>
                                                            )}
                                                            {caseItem.patientDob && (
                                                                <span>DOB: {formatDate(caseItem.patientDob, true)}</span>
                                                            )}
                                                        </div>
                                                    </div>
                                                </td>
                                                <td className="px-6 py-4">
                                                    <div>
                                                        <div className="text-sm font-medium text-slate-900 dark:text-white">
                                                            {caseItem.title || 'Untitled Case'}
                                                        </div>
                                                        <div className="text-xs text-slate-500 dark:text-slate-400 mt-1">
                                                            {caseItem.age && <span>{caseItem.age}</span>}
                                                            {caseItem.age && caseItem.sex && <span> {'\u00B7'} </span>}
                                                            {caseItem.sex && <span className="capitalize">{caseItem.sex}</span>}
                                                        </div>
                                                    </div>
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap">
                                                    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium border ${statusConfig.color}`}>
                                                        {statusConfig.icon}
                                                        {statusConfig.label}
                                                    </span>
                                                    {caseItem.errorMessage && (
                                                        <div className="text-xs text-red-500 mt-1 max-w-xs truncate" title={caseItem.errorMessage}>
                                                            {caseItem.errorMessage}
                                                        </div>
                                                    )}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500 dark:text-slate-400">
                                                    {formatDate(caseItem.createdAt)}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-right">
                                                    <div className="flex items-center justify-end gap-2">
                                                        {hasRadiopaediaLink && (
                                                            <a
                                                                href={getRadiopaediaUrl(caseItem.radiopaediaCaseId)}
                                                                target="_blank"
                                                                rel="noopener noreferrer"
                                                                className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-indigo-600 text-white text-xs font-medium rounded-lg hover:bg-indigo-700 transition-colors"
                                                            >
                                                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                                                                </svg>
                                                                View on Radiopaedia
                                                            </a>
                                                        )}
                                                        <button
                                                            onClick={() => handleOpenDetail(caseItem.id)}
                                                            className="p-1.5 rounded-md text-slate-400 hover:text-indigo-600 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
                                                            title="View case details"
                                                        >
                                                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                                                            </svg>
                                                        </button>
                                                    </div>
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>
                    </div>
                )}
            </div>

            {/* Case Detail Drawer */}
            <CaseDetailDrawer
                isOpen={drawerOpen}
                onClose={handleCloseDrawer}
                caseId={selectedCaseId}
            />
        </MainLayout>
    );
};

export default MyCasesPage;