import { useState, useMemo } from 'react';
import CaseDetailDrawer from '../CaseDetailDrawer';
import CaseLogsDrawer from './CaseLogsDrawer';

const STATUS_CONFIG = {
    Queued: {
        color: 'bg-yellow-100 text-yellow-800 border-yellow-200',
        icon: <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>,
        label: 'Queued',
    },
    Processing: {
        color: 'bg-blue-100 text-blue-800 border-blue-200',
        icon: <svg className="w-4 h-4 animate-spin" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" /></svg>,
        label: 'Processing',
    },
    Completed: {
        color: 'bg-green-100 text-green-800 border-green-200',
        icon: <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7" /></svg>,
        label: 'Completed',
    },
    Failed: {
        color: 'bg-red-100 text-red-800 border-red-200',
        icon: <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" /></svg>,
        label: 'Failed',
    },
};

const formatDate = (dateString, dateOnly = false) => {
    if (!dateString) return '';
    const options = dateOnly
        ? { day: '2-digit', month: '2-digit', year: '2-digit' }
        : { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' };
    return new Date(dateString).toLocaleDateString('en-AU', options);
};

/**
 * Shared cases table used by both MyCasesPage and AllCasesTab.
 *
 * Props:
 *   cases          – array of case objects
 *   loading        – boolean
 *   error          – string | null
 *   onRefresh      – () => void
 *   showUserColumn – show the "User" column (admin view)
 *   adminMode      – use admin endpoints for detail / logs drawers
 *   actions        – optional ReactNode rendered after the Refresh button (e.g. "Add New Case")
 *   loadingMessage – override the loading text
 */
const CasesTable = ({
    cases,
    loading,
    error,
    onRefresh,
    showUserColumn = false,
    adminMode = false,
    actions = null,
    loadingMessage = 'Loading cases...',
}) => {
    const [searchQuery, setSearchQuery] = useState('');
    const [statusFilter, setStatusFilter] = useState(null);

    const [detailOpen, setDetailOpen] = useState(false);
    const [selectedCaseId, setSelectedCaseId] = useState(null);

    const [logsOpen, setLogsOpen] = useState(false);
    const [logsCase, setLogsCase] = useState(null);

    const completedCount = cases.filter(c => c.status === 'Completed').length;
    const pendingCount  = cases.filter(c => c.status === 'Queued' || c.status === 'Processing').length;
    const failedCount   = cases.filter(c => c.status === 'Failed').length;

    const filteredCases = useMemo(() => {
        return cases.filter(c => {
            if (statusFilter === 'Completed' && c.status !== 'Completed') return false;
            if (statusFilter === 'Pending' && c.status !== 'Queued' && c.status !== 'Processing') return false;
            if (statusFilter === 'Failed' && c.status !== 'Failed') return false;
            if (searchQuery.trim()) {
                const q = searchQuery.toLowerCase();
                return (
                    (c.title || '').toLowerCase().includes(q) ||
                    (c.patientName || '').toLowerCase().includes(q) ||
                    (c.patientId || '').toLowerCase().includes(q) ||
                    (showUserColumn && (c.username || '').toLowerCase().includes(q))
                );
            }
            return true;
        });
    }, [cases, statusFilter, searchQuery, showUserColumn]);

    const toggleStatusFilter = (filter) =>
        setStatusFilter(prev => prev === filter ? null : filter);

    const clearFilters = () => { setStatusFilter(null); setSearchQuery(''); };

    return (
        <>
            {/* Toolbar: search + refresh + optional extra actions */}
            <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3 mb-4">
                <div className="relative">
                    <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                    </svg>
                    <input
                        type="text"
                        placeholder={showUserColumn ? 'Search title, patient, user…' : 'Search title, patient name, or ID…'}
                        value={searchQuery}
                        onChange={e => setSearchQuery(e.target.value)}
                        className="pl-9 pr-8 py-2 w-56 sm:w-72 border border-slate-300 dark:border-slate-600 rounded-lg text-sm bg-white dark:bg-slate-800 text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                    />
                    {searchQuery && (
                        <button onClick={() => setSearchQuery('')} className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600">
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    )}
                </div>
                <div className="flex items-center gap-2">
                    <button
                        onClick={onRefresh}
                        disabled={loading}
                        className="flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors disabled:opacity-50 text-sm"
                    >
                        <svg className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                        </svg>
                        Refresh
                    </button>
                    {actions}
                </div>
            </div>

            {/* Summary cards */}
            {!loading && !error && cases.length > 0 && (
                <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
                    {[
                        { label: 'Total', count: cases.length, filter: null, activeClass: 'border-indigo-500 ring-2 ring-indigo-500/20', inactiveClass: 'border-slate-200 dark:border-slate-700', countClass: 'text-slate-900 dark:text-white' },
                        { label: 'Completed', count: completedCount, filter: 'Completed', dot: 'bg-green-500', activeClass: 'border-green-500 ring-2 ring-green-500/20', inactiveClass: 'border-green-200 dark:border-green-800', countClass: 'text-green-600 dark:text-green-400' },
                        { label: 'Pending',   count: pendingCount,   filter: 'Pending',   dot: 'bg-yellow-500', activeClass: 'border-yellow-500 ring-2 ring-yellow-500/20', inactiveClass: 'border-yellow-200 dark:border-yellow-800', countClass: 'text-yellow-600 dark:text-yellow-400' },
                        { label: 'Failed',    count: failedCount,    filter: 'Failed',    dot: 'bg-red-500',    activeClass: 'border-red-500 ring-2 ring-red-500/20',       inactiveClass: 'border-red-200 dark:border-red-800',       countClass: 'text-red-600 dark:text-red-400' },
                    ].map(({ label, count, filter, dot, activeClass, inactiveClass, countClass }) => (
                        <button
                            key={label}
                            onClick={() => filter === null ? clearFilters() : toggleStatusFilter(filter)}
                            className={`bg-white dark:bg-slate-800 rounded-lg shadow border px-3 py-2 text-left transition-all hover:shadow-md flex items-center justify-between ${statusFilter === filter ? activeClass : inactiveClass}`}
                        >
                            <span className="flex items-center gap-1.5">
                                {dot && <span className={`w-2 h-2 rounded-full ${dot}`} />}
                                <span className="text-xs text-slate-500 dark:text-slate-400">{label}</span>
                            </span>
                            <span className={`text-lg font-bold ${countClass}`}>{count}</span>
                        </button>
                    ))}
                </div>
            )}

            {/* Active filter pills */}
            {(statusFilter || searchQuery) && (
                <div className="mb-4 flex items-center gap-2 text-sm text-slate-600 dark:text-slate-400">
                    <span>Showing:</span>
                    {statusFilter && (
                        <span className="inline-flex items-center gap-1 px-2 py-1 bg-slate-100 dark:bg-slate-700 rounded">
                            {statusFilter}
                            <button onClick={() => setStatusFilter(null)} className="hover:text-red-500">
                                <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" /></svg>
                            </button>
                        </span>
                    )}
                    {searchQuery && (
                        <span className="inline-flex items-center gap-1 px-2 py-1 bg-slate-100 dark:bg-slate-700 rounded">
                            &ldquo;{searchQuery}&rdquo;
                            <button onClick={() => setSearchQuery('')} className="hover:text-red-500">
                                <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" /></svg>
                            </button>
                        </span>
                    )}
                    <button onClick={clearFilters} className="text-indigo-600 hover:text-indigo-700 ml-1">Clear all</button>
                </div>
            )}

            {/* Error */}
            {error && (
                <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4 mb-4">
                    <div className="flex items-center gap-2 text-red-700 dark:text-red-400">
                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
                        <span>{error}</span>
                    </div>
                </div>
            )}

            {/* Loading */}
            {loading && (
                <div className="bg-white dark:bg-slate-800 rounded-lg shadow p-12 flex flex-col items-center justify-center">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600 mb-4" />
                    <p className="text-slate-500">{loadingMessage}</p>
                </div>
            )}

            {/* Empty */}
            {!loading && !error && cases.length === 0 && (
                <div className="bg-white dark:bg-slate-800 rounded-lg shadow border border-slate-200 dark:border-slate-700 p-12 text-center">
                    <svg className="w-16 h-16 mx-auto text-slate-300 dark:text-slate-600 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                    </svg>
                    <h3 className="text-lg font-medium text-slate-900 dark:text-white mb-2">No cases yet</h3>
                    <p className="text-slate-500 dark:text-slate-400">Cases you submit will appear here.</p>
                </div>
            )}

            {/* No results from filter */}
            {!loading && !error && cases.length > 0 && filteredCases.length === 0 && (
                <div className="bg-white dark:bg-slate-800 rounded-lg shadow border border-slate-200 dark:border-slate-700 p-12 text-center">
                    <svg className="w-16 h-16 mx-auto text-slate-300 dark:text-slate-600 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                    </svg>
                    <h3 className="text-lg font-medium text-slate-900 dark:text-white mb-2">No matching cases</h3>
                    <p className="text-slate-500 dark:text-slate-400">Try adjusting your search or filters.</p>
                    <button onClick={clearFilters} className="inline-flex items-center gap-2 mt-4 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors">
                        Clear Filters
                    </button>
                </div>
            )}

            {/* Table */}
            {!loading && !error && filteredCases.length > 0 && (
                <div className="bg-white dark:bg-slate-800 rounded-lg shadow border border-slate-200 dark:border-slate-700 overflow-hidden">
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-slate-200 dark:divide-slate-700">
                            <thead className="bg-slate-50 dark:bg-slate-900/50">
                                <tr>
                                    {showUserColumn && (
                                        <th className="px-4 py-3 text-left text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">User</th>
                                    )}
                                    <th className="px-4 py-3 text-left text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">Patient</th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">Case</th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">Status</th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">Created</th>
                                    <th className="px-4 py-3 text-right text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">Actions</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-200 dark:divide-slate-700">
                                {filteredCases.map(c => {
                                    const cfg = STATUS_CONFIG[c.status] ?? STATUS_CONFIG.Queued;
                                    const hasLink = c.status === 'Completed' && c.radiopaediaCaseId;
                                    return (
                                        <tr key={c.id} className="hover:bg-slate-50 dark:hover:bg-slate-700/50 transition-colors">
                                            {showUserColumn && (
                                                <td className="px-4 py-3">
                                                    <span className="text-xs font-mono bg-slate-100 dark:bg-slate-700 text-slate-700 dark:text-slate-300 px-1.5 py-0.5 rounded">
                                                        {c.username || '—'}
                                                    </span>
                                                </td>
                                            )}
                                            <td className="px-4 py-3">
                                                <div className="text-sm font-medium text-slate-900 dark:text-white">
                                                    {(c.patientName || '—').replace(/\^/g, ' ')}
                                                </div>
                                                <div className="text-xs text-slate-500 dark:text-slate-400 mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5">
                                                    {c.patientId && (
                                                        <span className="font-mono bg-slate-100 dark:bg-slate-700 px-1.5 py-0.5 rounded">{c.patientId}</span>
                                                    )}
                                                    {c.patientDob && <span>DOB: {formatDate(c.patientDob, true)}</span>}
                                                </div>
                                            </td>
                                            <td className="px-4 py-3">
                                                <div className="text-sm font-medium text-slate-900 dark:text-white">{c.title || 'Untitled Case'}</div>
                                                <div className="text-xs text-slate-500 dark:text-slate-400 mt-1">
                                                    {c.age && <span>{c.age}</span>}
                                                    {c.age && c.sex && <span> · </span>}
                                                    {c.sex && <span className="capitalize">{c.sex}</span>}
                                                </div>
                                            </td>
                                            <td className="px-4 py-3 whitespace-nowrap">
                                                <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium border ${cfg.color}`}>
                                                    {cfg.icon}
                                                    {cfg.label}
                                                </span>
                                                {c.errorMessage && (
                                                    <div className="text-xs text-red-500 mt-1 truncate max-w-0 min-w-full" title={c.errorMessage}>
                                                        {c.errorMessage}
                                                    </div>
                                                )}
                                            </td>
                                            <td className="px-4 py-3 whitespace-nowrap text-sm text-slate-500 dark:text-slate-400">
                                                {formatDate(c.createdAt)}
                                            </td>
                                            <td className="px-4 py-3 whitespace-nowrap text-right">
                                                <div className="flex items-center justify-end gap-1.5">
                                                    {hasLink && (
                                                        <a
                                                            href={`https://radiopaedia.org/cases/${c.radiopaediaCaseId}`}
                                                            target="_blank"
                                                            rel="noopener noreferrer"
                                                            className="inline-flex items-center gap-1 px-2.5 py-1.5 bg-indigo-600 text-white text-xs font-medium rounded-lg hover:bg-indigo-700 transition-colors"
                                                        >
                                                            <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                                                            </svg>
                                                            Radiopaedia
                                                        </a>
                                                    )}
                                                    <button
                                                        onClick={() => { setLogsCase(c); setLogsOpen(true); }}
                                                        className="p-1.5 rounded-md text-slate-400 hover:text-amber-600 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
                                                        title="View case logs"
                                                    >
                                                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                                                        </svg>
                                                    </button>
                                                    <button
                                                        onClick={() => { setSelectedCaseId(c.id); setDetailOpen(true); }}
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

            {/* Drawers */}
            <CaseDetailDrawer
                isOpen={detailOpen}
                onClose={() => { setDetailOpen(false); setSelectedCaseId(null); }}
                caseId={selectedCaseId}
                adminMode={adminMode}
            />
            <CaseLogsDrawer
                isOpen={logsOpen}
                onClose={() => { setLogsOpen(false); setLogsCase(null); }}
                caseId={logsCase?.id}
                caseTitle={logsCase?.title}
                apiBase={adminMode ? '/api/logs/case' : '/api/cases'}
                pathSuffix={adminMode ? '' : '/logs'}
            />
        </>
    );
};

export default CasesTable;
