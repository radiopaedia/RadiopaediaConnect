import { useState, useEffect } from 'react';

const LEVEL_STYLES = {
    Error: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400',
    Warning: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-400',
    Information: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400',
};

const MSG_DETAIL_THRESHOLD = 60; // chars — show expand chevron when message may be cut off

const CaseLogsDrawer = ({ isOpen, onClose, caseId, caseTitle, apiBase = '/api/logs/case', pathSuffix = '' }) => {
    const [logs, setLogs] = useState([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);
    const [totalCount, setTotalCount] = useState(0);
    const [page, setPage] = useState(1);
    const [totalPages, setTotalPages] = useState(1);
    const [expandedRows, setExpandedRows] = useState(new Set());

    const PAGE_SIZE = 100;

    useEffect(() => {
        if (isOpen && caseId) {
            setPage(1);
            setExpandedRows(new Set());
            fetchLogs(1);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [isOpen, caseId]);

    useEffect(() => {
        document.body.style.overflow = isOpen ? 'hidden' : '';
        return () => { document.body.style.overflow = ''; };
    }, [isOpen]);

    const fetchLogs = async (p) => {
        setLoading(true);
        setError(null);
        try {
            const res = await fetch(`${apiBase}/${caseId}${pathSuffix}?page=${p}&pageSize=${PAGE_SIZE}`);
            if (!res.ok) throw new Error('Failed to fetch logs');
            const data = await res.json();
            setLogs(data.items);
            setTotalCount(data.totalCount);
            setTotalPages(data.totalPages);
        } catch (err) {
            setError(err.message);
        } finally {
            setLoading(false);
        }
    };

    const handlePageChange = (newPage) => {
        setPage(newPage);
        fetchLogs(newPage);
    };

    const toggleRow = (id) => {
        setExpandedRows(prev => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id);
            else next.add(id);
            return next;
        });
    };

    const formatTs = (ts) =>
        new Date(ts).toLocaleString('en-AU', {
            month: 'short', day: 'numeric',
            hour: '2-digit', minute: '2-digit', second: '2-digit',
        });

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 z-50 flex justify-end">
            <div className="absolute inset-0 bg-black/30" onClick={onClose} />
            <div className="relative w-full max-w-3xl bg-white dark:bg-slate-900 shadow-xl flex flex-col h-full overflow-hidden">
                {/* Header */}
                <div className="flex items-center justify-between px-6 py-4 border-b border-slate-200 dark:border-slate-700 flex-shrink-0">
                    <div>
                        <h2 className="text-lg font-semibold text-slate-900 dark:text-white">Case Logs</h2>
                        <p className="text-xs text-slate-400 dark:text-slate-500 mt-0.5 font-mono truncate max-w-md">
                            {caseId}{caseTitle ? <span className="font-sans text-slate-500 dark:text-slate-400"> | {caseTitle}</span> : null}
                        </p>
                    </div>
                    <button
                        onClick={onClose}
                        className="p-2 rounded-md text-slate-400 hover:text-slate-600 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
                    >
                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                </div>

                {/* Toolbar */}
                <div className="flex items-center justify-between px-6 py-3 border-b border-slate-200 dark:border-slate-700 flex-shrink-0 bg-slate-50 dark:bg-slate-800/50">
                    <span className="text-sm text-slate-500 dark:text-slate-400">
                        {totalCount > 0 ? `${totalCount} log ${totalCount === 1 ? 'entry' : 'entries'}` : 'No logs'}
                    </span>
                    <button
                        onClick={() => fetchLogs(page)}
                        disabled={loading}
                        className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md border border-slate-300 dark:border-slate-600 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors disabled:opacity-50"
                    >
                        <svg className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                        </svg>
                        Refresh
                    </button>
                </div>

                {/* Content */}
                <div className="flex-1 overflow-y-auto">
                    {loading && (
                        <div className="flex items-center justify-center py-16">
                            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
                        </div>
                    )}

                    {error && (
                        <div className="m-6 px-4 py-3 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg text-sm text-red-700 dark:text-red-400">
                            {error}
                        </div>
                    )}

                    {!loading && !error && logs.length === 0 && (
                        <div className="flex flex-col items-center justify-center py-16 text-slate-400">
                            <svg className="w-12 h-12 mb-3 opacity-40" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                            </svg>
                            <p className="text-sm">No logs found for this case.</p>
                        </div>
                    )}

                    {!loading && logs.length > 0 && (
                        <table className="w-full table-fixed text-xs">
                            <thead className="bg-slate-50 dark:bg-slate-800 sticky top-0">
                                <tr>
                                    <th className="px-4 py-2 text-left font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider w-36">Time</th>
                                    <th className="px-4 py-2 text-left font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider w-24">Level</th>
                                    <th className="px-4 py-2 text-left font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">Message</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                                {logs.map((log) => {
                                    const isExpanded = expandedRows.has(log.id);
                                    const hasDetail = log.message.length > MSG_DETAIL_THRESHOLD || !!log.exception;

                                    return (
                                        <>
                                            <tr
                                                key={log.id}
                                                className={`hover:bg-slate-50 dark:hover:bg-slate-800/50 ${hasDetail ? 'cursor-pointer' : ''}`}
                                                onClick={() => hasDetail && toggleRow(log.id)}
                                            >
                                                <td className="px-4 py-2 font-mono text-slate-500 dark:text-slate-400 whitespace-nowrap">
                                                    {formatTs(log.timestampUtc)}
                                                </td>
                                                <td className="px-4 py-2 whitespace-nowrap">
                                                    <span className={`inline-block px-1.5 py-0.5 rounded text-xs font-medium ${LEVEL_STYLES[log.level] ?? LEVEL_STYLES.Information}`}>
                                                        {log.level}
                                                    </span>
                                                </td>
                                                <td className="px-4 py-2 text-slate-800 dark:text-slate-200 min-w-0">
                                                    <div className="flex items-center gap-1 min-w-0">
                                                        {hasDetail && (
                                                            <svg className={`w-3 h-3 flex-shrink-0 text-slate-400 transition-transform ${isExpanded ? 'rotate-90' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5l7 7-7 7" />
                                                            </svg>
                                                        )}
                                                        <span className="truncate">{log.message}</span>
                                                    </div>
                                                </td>
                                            </tr>
                                            {isExpanded && (
                                                <tr key={`${log.id}-detail`} className="bg-slate-50 dark:bg-slate-800/30">
                                                    <td colSpan={3} className="px-6 py-3 space-y-2">
                                                        <p className="text-slate-700 dark:text-slate-300 whitespace-pre-wrap break-words leading-relaxed">
                                                            {log.message}
                                                        </p>
                                                        {log.exception && (
                                                            <pre className="text-xs text-red-700 dark:text-red-400 bg-red-50 dark:bg-red-900/20 rounded p-3 whitespace-pre-wrap break-words">
                                                                {log.exception}
                                                            </pre>
                                                        )}
                                                    </td>
                                                </tr>
                                            )}
                                        </>
                                    );
                                })}
                            </tbody>
                        </table>
                    )}
                </div>

                {/* Pagination */}
                {totalPages > 1 && (
                    <div className="flex items-center justify-between px-6 py-3 border-t border-slate-200 dark:border-slate-700 flex-shrink-0">
                        <span className="text-xs text-slate-500 dark:text-slate-400">
                            Page {page} of {totalPages}
                        </span>
                        <div className="flex gap-2">
                            <button
                                onClick={() => handlePageChange(page - 1)}
                                disabled={page <= 1}
                                className="px-3 py-1.5 text-xs rounded-md border border-slate-300 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 disabled:opacity-40 transition-colors"
                            >
                                Previous
                            </button>
                            <button
                                onClick={() => handlePageChange(page + 1)}
                                disabled={page >= totalPages}
                                className="px-3 py-1.5 text-xs rounded-md border border-slate-300 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 disabled:opacity-40 transition-colors"
                            >
                                Next
                            </button>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
};

export default CaseLogsDrawer;
