import { useState, useEffect, useCallback } from 'react';
import {
    useReactTable,
    getCoreRowModel,
    flexRender,
    createColumnHelper,
} from '@tanstack/react-table';

const columnHelper = createColumnHelper();

const LEVEL_STYLES = {
    Error:       'bg-red-100 text-red-800 dark:bg-red-900/40 dark:text-red-300',
    Warning:     'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/40 dark:text-yellow-300',
    Information: 'bg-blue-100 text-blue-800 dark:bg-blue-900/40 dark:text-blue-300',
};

const LEVELS = ['', 'Error', 'Warning', 'Information'];

function formatUtc(ts) {
    if (!ts) return '';
    try {
        return new Date(ts).toLocaleString('en-AU', { dateStyle: 'short', timeStyle: 'medium' });
    } catch {
        return ts;
    }
}

const columns = [
    columnHelper.accessor('timestampUtc', {
        header: 'Timestamp',
        size: 160,
        cell: (info) => (
            <span className="text-xs text-slate-500 dark:text-slate-400 whitespace-nowrap">
                {formatUtc(info.getValue())}
            </span>
        ),
    }),
    columnHelper.accessor('level', {
        header: 'Level',
        size: 90,
        cell: (info) => {
            const level = info.getValue();
            return (
                <span className={`inline-block px-2 py-0.5 rounded text-xs font-medium ${LEVEL_STYLES[level] ?? 'bg-slate-100 text-slate-600'}`}>
                    {level}
                </span>
            );
        },
    }),
    columnHelper.accessor('category', {
        header: 'Category',
        size: 100,
        cell: (info) => (
            <span className="text-xs font-medium text-slate-600 dark:text-slate-400">{info.getValue()}</span>
        ),
    }),
    columnHelper.accessor('message', {
        header: 'Message',
        cell: (info) => (
            <span className="text-xs text-slate-800 dark:text-slate-200 break-words">{info.getValue()}</span>
        ),
    }),
];

const LogsTab = () => {
    const [logs, setLogs]               = useState([]);
    const [totalCount, setTotalCount]   = useState(0);
    const [totalPages, setTotalPages]   = useState(1);
    const [page, setPage]               = useState(1);
    const [pageSize]                    = useState(100);
    const [startDate, setStartDate]     = useState('');
    const [endDate, setEndDate]         = useState('');
    const [levelFilter, setLevelFilter] = useState('');
    const [loading, setLoading]         = useState(false);
    const [expandedRow, setExpandedRow] = useState(null);

    const fetchLogs = useCallback(async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({ page, pageSize });
            if (startDate) params.set('startDate', startDate);
            if (endDate)   params.set('endDate', endDate);
            if (levelFilter) params.set('level', levelFilter);

            const res = await fetch(`/api/logs?${params}`);
            if (res.ok) {
                const data = await res.json();
                setLogs(data.items ?? []);
                setTotalCount(data.totalCount ?? 0);
                setTotalPages(data.totalPages ?? 1);
            }
        } catch (err) {
            console.error('Failed to fetch logs', err);
        } finally {
            setLoading(false);
        }
    }, [page, pageSize, startDate, endDate, levelFilter]);

    useEffect(() => {
        fetchLogs();
    }, [fetchLogs]);

    // Reset to page 1 when filters change
    const handleFilterChange = (setter) => (e) => {
        setter(e.target.value);
        setPage(1);
    };

    const table = useReactTable({
        data: logs,
        columns,
        getCoreRowModel: getCoreRowModel(),
        manualPagination: true,
        pageCount: totalPages,
    });

    return (
        <div className="space-y-4">
            <div className="flex items-center justify-between flex-wrap gap-3">
                <div>
                    <h2 className="text-lg font-semibold">Application Logs</h2>
                    <p className="text-sm text-slate-500 dark:text-slate-400">
                        Pipeline, API, DICOM and system events. Retained for 30 days.
                    </p>
                </div>
                <button
                    onClick={fetchLogs}
                    className="px-3 py-1.5 text-sm font-medium rounded-md border border-slate-300 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
                >
                    Refresh
                </button>
            </div>

            {/* Filters */}
            <div className="flex flex-wrap gap-3 items-end">
                <div>
                    <label className="block text-xs font-medium text-slate-500 mb-1">From</label>
                    <input
                        type="date"
                        value={startDate}
                        onChange={handleFilterChange(setStartDate)}
                        className="rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-2 py-1.5 text-sm shadow-sm"
                    />
                </div>
                <div>
                    <label className="block text-xs font-medium text-slate-500 mb-1">To</label>
                    <input
                        type="date"
                        value={endDate}
                        onChange={handleFilterChange(setEndDate)}
                        className="rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-2 py-1.5 text-sm shadow-sm"
                    />
                </div>
                <div>
                    <label className="block text-xs font-medium text-slate-500 mb-1">Level</label>
                    <select
                        value={levelFilter}
                        onChange={handleFilterChange(setLevelFilter)}
                        className="rounded-md border border-slate-300 dark:border-slate-600 dark:bg-slate-900 px-2 py-1.5 text-sm shadow-sm"
                    >
                        {LEVELS.map((l) => (
                            <option key={l} value={l}>{l || 'All Levels'}</option>
                        ))}
                    </select>
                </div>
                {(startDate || endDate || levelFilter) && (
                    <button
                        onClick={() => { setStartDate(''); setEndDate(''); setLevelFilter(''); setPage(1); }}
                        className="px-3 py-1.5 text-xs font-medium text-slate-500 hover:text-slate-800 dark:hover:text-slate-200 transition-colors"
                    >
                        Clear filters
                    </button>
                )}
                <span className="ml-auto text-xs text-slate-400 self-end pb-1.5">
                    {totalCount.toLocaleString()} total
                </span>
            </div>

            {/* Table */}
            <div className="overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-700">
                <table className="w-full text-sm">
                    <thead>
                        {table.getHeaderGroups().map((headerGroup) => (
                            <tr key={headerGroup.id} className="bg-slate-50 dark:bg-slate-800/60 border-b border-slate-200 dark:border-slate-700">
                                {headerGroup.headers.map((header) => (
                                    <th
                                        key={header.id}
                                        className="px-3 py-2 text-left text-xs font-semibold text-slate-600 dark:text-slate-400 whitespace-nowrap"
                                        style={{ width: header.column.columnDef.size }}
                                    >
                                        {flexRender(header.column.columnDef.header, header.getContext())}
                                    </th>
                                ))}
                            </tr>
                        ))}
                    </thead>
                    <tbody>
                        {loading ? (
                            <tr>
                                <td colSpan={4} className="px-3 py-8 text-center text-slate-400 text-sm">
                                    Loading...
                                </td>
                            </tr>
                        ) : table.getRowModel().rows.length === 0 ? (
                            <tr>
                                <td colSpan={4} className="px-3 py-8 text-center text-slate-400 text-sm">
                                    No logs found.
                                </td>
                            </tr>
                        ) : (
                            table.getRowModel().rows.map((row) => {
                                const isExpanded = expandedRow === row.id;
                                return (
                                    <>
                                        <tr
                                            key={row.id}
                                            onClick={() => setExpandedRow(isExpanded ? null : row.id)}
                                            className="border-b border-slate-100 dark:border-slate-700/50 hover:bg-slate-50 dark:hover:bg-slate-800/40 cursor-pointer transition-colors"
                                        >
                                            {row.getVisibleCells().map((cell) => (
                                                <td key={cell.id} className="px-3 py-2 align-top max-w-0">
                                                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                                                </td>
                                            ))}
                                        </tr>
                                        {isExpanded && (row.original.exception || row.original.jobId) && (
                                            <tr key={`${row.id}-exp`} className="bg-slate-50 dark:bg-slate-800/60 border-b border-slate-200 dark:border-slate-700">
                                                <td colSpan={4} className="px-4 py-3 space-y-2">
                                                    {row.original.jobId && (
                                                        <p className="text-xs text-slate-500">
                                                            <span className="font-medium">Job ID:</span> {row.original.jobId}
                                                        </p>
                                                    )}
                                                    {row.original.exception && (
                                                        <pre className="text-xs text-red-700 dark:text-red-400 bg-red-50 dark:bg-red-900/20 rounded p-2 overflow-x-auto whitespace-pre-wrap break-words max-h-48">
                                                            {row.original.exception}
                                                        </pre>
                                                    )}
                                                </td>
                                            </tr>
                                        )}
                                    </>
                                );
                            })
                        )}
                    </tbody>
                </table>
            </div>

            {/* Pagination */}
            {totalPages > 1 && (
                <div className="flex items-center justify-between text-sm">
                    <button
                        onClick={() => setPage((p) => Math.max(1, p - 1))}
                        disabled={page <= 1}
                        className="px-3 py-1.5 rounded-md border border-slate-300 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors disabled:opacity-40"
                    >
                        Previous
                    </button>
                    <span className="text-slate-500 text-xs">
                        Page {page} of {totalPages}
                    </span>
                    <button
                        onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                        disabled={page >= totalPages}
                        className="px-3 py-1.5 rounded-md border border-slate-300 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors disabled:opacity-40"
                    >
                        Next
                    </button>
                </div>
            )}
        </div>
    );
};

export default LogsTab;
