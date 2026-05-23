import { useState, useEffect, useRef } from 'react';
import DicomViewer from './DicomViewer';
// AnonymisationDrawer lives in DashboardPage now (global per-case setting)
import { clearFileCache } from '../../../lib/csdicomLoader';

// Modalities where multiframe instances are common — require preview before checkbox selection
const MULTIFRAME_MODALITIES = new Set(['US', 'XA', 'RF', 'NM', 'PT', 'IVUS', 'SC', 'OT']);

const SeriesPicker = ({ seriesList, selectedSeriesMap, onSeriesUpdate, loading }) => {
    const [activeSeries, setActiveSeries] = useState(null);

    // Local state for the slice sliders (Subset Logic)
    const [sliceConfig, setSliceConfig] = useState({ start: 1, end: 1, step: 1 });

    // Local state for the viewer navigation
    const [currentFrameIndex, setCurrentFrameIndex] = useState(0);

    const [previewJob, setPreviewJob] = useState({
        status: 'idle',
        serverStatus: '',
        jobId: null,
        error: null
    });
    const [previewImageIds, setPreviewImageIds] = useState([]);

    // Metadata cache keyed by seriesInstanceUid (persists across series switches)
    const [seriesMetadataMap, setSeriesMetadataMap] = useState({});

    // Redaction State
    const [isRedacting, setIsRedacting] = useState(false);
    const [hasRedactionSelected, setHasRedactionSelected] = useState(false);
    const [hasRedactions, setHasRedactions] = useState(false);

    // Holds the redactions to be drawn when the viewer first loads (restored from save)
    const [restoredRedactions, setRestoredRedactions] = useState([]);

    const viewerRef = useRef(null);
    const timelineRef = useRef(null);
    const isDragging = useRef(null);
    const shouldAutoPreview = useRef(false);

    // Active series metadata (from the per-series cache)
    const activeMetadata = activeSeries ? seriesMetadataMap[activeSeries.seriesInstanceUid] : null;

    // Use totalFrameCount from metadata when available, fall back to instanceCount
    const effectiveFrameCount = activeMetadata?.totalFrameCount ?? activeSeries?.instanceCount ?? 0;

    /**
     * Fetches series metadata and builds csdicom: image IDs.
     * Returns { ids, metadata } or null on failure.
     */
    const fetchMetadataAndBuildImageIds = async (seriesUid) => {
        const res = await fetch(`/api/cornerstone/series/${seriesUid}/metadata`);
        if (!res.ok) return null;
        const metadata = await res.json();
        const ids = metadata.expandedFrames.map(f =>
            `csdicom:${seriesUid}|${f.fileName}|${f.frameIndex}`
        );
        return { ids, metadata };
    };

    useEffect(() => {
        if (!activeSeries) return;

        let isMounted = true;

        // Clear previous series' file cache to free memory
        clearFileCache();

        setPreviewJob({ status: 'idle', serverStatus: '', jobId: null, error: null });
        setPreviewImageIds([]);
        setIsRedacting(false);
        setHasRedactionSelected(false);
        setHasRedactions(false);
        setCurrentFrameIndex(0);

        const savedState = selectedSeriesMap[activeSeries.seriesInstanceUid];

        if (savedState) {
            setSliceConfig({
                start: savedState.start,
                end: savedState.end,
                step: savedState.step
            });
            setCurrentFrameIndex(Math.max(0, savedState.start - 1));
            setRestoredRedactions(savedState.redactions || []);
        } else {
            setSliceConfig({
                start: 1,
                end: activeSeries.instanceCount,
                step: 1
            });
            setCurrentFrameIndex(0);
            setRestoredRedactions([]);
        }

        const checkAvailability = async () => {
            if (shouldAutoPreview.current) {
                setPreviewJob({ status: 'loading', serverStatus: 'Requesting...', jobId: null, error: null });
                try {
                    const response = await fetch('/api/dicom/preview', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            studyInstanceUid: activeSeries.studyInstanceUid,
                            seriesInstanceUid: activeSeries.seriesInstanceUid,
                        })
                    });
                    if (!isMounted) return;
                    if (!response.ok) throw new Error("Failed to initiate preview");
                    const result = await response.json();

                    if (result.status === 'Ready') {
                        const data = await fetchMetadataAndBuildImageIds(activeSeries.seriesInstanceUid);
                        if (data && isMounted) {
                            setSeriesMetadataMap(prev => ({ ...prev, [activeSeries.seriesInstanceUid]: data.metadata }));
                            setPreviewImageIds(data.ids);
                            setPreviewJob({ status: 'ready', serverStatus: 'Ready', jobId: null, error: null });
                            // Auto-update slice config if frame count differs from instanceCount
                            autoUpdateFrameCount(data.metadata, activeSeries, savedState);
                        }
                    } else {
                        setPreviewJob({ status: 'loading', serverStatus: result.status, jobId: result.jobId, error: null });
                    }
                } catch (error) {
                    if (isMounted) setPreviewJob({ status: 'error', jobId: null, error: "Network Error: " + error });
                }
            } else {
                try {
                    const data = await fetchMetadataAndBuildImageIds(activeSeries.seriesInstanceUid);
                    if (data && isMounted) {
                        setSeriesMetadataMap(prev => ({ ...prev, [activeSeries.seriesInstanceUid]: data.metadata }));
                        setPreviewImageIds(data.ids);
                        setPreviewJob({ status: 'ready', serverStatus: 'Ready', jobId: null, error: null });
                        autoUpdateFrameCount(data.metadata, activeSeries, savedState);
                    }
                } catch (error) {
                    console.warn("Auto-preview check failed", error);
                }
            }
        };

        checkAvailability();

        return () => { isMounted = false; };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [activeSeries]); // Dependency on activeSeries IDENTITY only

    /**
     * When metadata reveals totalFrameCount != instanceCount, auto-update
     * the slice config end value and any saved selection.
     */
    const autoUpdateFrameCount = (metadata, series, savedState) => {
        if (!metadata || metadata.totalFrameCount === series.instanceCount) return;

        const totalFrames = metadata.totalFrameCount;

        if (savedState) {
            // If the saved end was the old instanceCount, extend to totalFrameCount
            if (savedState.end === series.instanceCount) {
                setSliceConfig(prev => ({ ...prev, end: totalFrames }));
                onSeriesUpdate(series.seriesInstanceUid, {
                    ...savedState,
                    end: totalFrames,
                    total: Math.floor((totalFrames - savedState.start) / savedState.step) + 1
                }, 'select');
            }
        } else {
            // No saved state: update the default end
            setSliceConfig(prev => {
                if (prev.end === series.instanceCount) {
                    return { ...prev, end: totalFrames };
                }
                return prev;
            });
        }
    };

    useEffect(() => {
        let intervalId;
        if (previewJob.status === 'loading' && previewJob.jobId) {
            intervalId = setInterval(async () => {
                try {
                    const res = await fetch(`/api/dicom/status/${previewJob.jobId}`);
                    if (!res.ok) throw new Error("Status check failed");
                    const data = await res.json();

                    setPreviewJob(prev => ({ ...prev, serverStatus: data.status }));

                    if (data.status === 'Completed') {
                        clearInterval(intervalId);
                        try {
                            const result = await fetchMetadataAndBuildImageIds(activeSeries.seriesInstanceUid);
                            if (!result) throw new Error("Failed to fetch image list");
                            setSeriesMetadataMap(prev => ({ ...prev, [activeSeries.seriesInstanceUid]: result.metadata }));
                            setPreviewImageIds(result.ids);
                            setPreviewJob(prev => ({ ...prev, status: 'ready', serverStatus: 'Completed' }));
                            const savedState = selectedSeriesMap[activeSeries.seriesInstanceUid];
                            autoUpdateFrameCount(result.metadata, activeSeries, savedState);
                        } catch {
                            setPreviewJob(prev => ({ ...prev, status: 'error', error: "Failed to load images" }));
                        }
                    } else if (data.status === 'Failed' || data.status === 'Cancelled') {
                        setPreviewJob(prev => ({ ...prev, status: 'error', error: data.errorMessage || 'Job Failed' }));
                        clearInterval(intervalId);
                    }
                } catch (err) {
                    console.error("Polling error", err);
                }
            }, 2000);
        }
        return () => { if (intervalId) clearInterval(intervalId); };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [previewJob.status, previewJob.jobId, activeSeries]);

    const handleSubsetSave = () => {
        if (!activeSeries) return;
        const count = Math.floor((sliceConfig.end - sliceConfig.start) / sliceConfig.step) + 1;
        const savedState = selectedSeriesMap[activeSeries.seriesInstanceUid];
        const existingRedactions = savedState?.redactions || [];
        // Preserve an existing forced-PNG flag (e.g. from earlier redaction),
        // or add one if this subset configuration makes DICOM unavailable.
        const wasForcedPng = savedState?.uploadMethod === 'png';

        onSeriesUpdate(
            activeSeries.seriesInstanceUid,
            {
                ...sliceConfig,
                total: count,
                redactions: existingRedactions,
                // Only stamp uploadMethod:'png' when forced; omit otherwise so the
                // global case-level setting in DashboardPage takes effect.
                ...((wasForcedPng || !dicomAvailable) ? { uploadMethod: 'png' } : {})
            },
            'select'
        );
    };

    const handleRedactionToggle = () => {
        if (isRedacting) {
            if (activeSeries && viewerRef.current) {
                const newRedactions = viewerRef.current.getRedactionData();
                const savedState = selectedSeriesMap[activeSeries.seriesInstanceUid];
                const configToUse = savedState
                    ? { start: savedState.start, end: savedState.end, step: savedState.step, total: savedState.total }
                    : {
                        start: sliceConfig.start,
                        end: sliceConfig.end,
                        step: sliceConfig.step,
                        total: Math.floor((sliceConfig.end - sliceConfig.start) / sliceConfig.step) + 1
                    };

                onSeriesUpdate(
                    activeSeries.seriesInstanceUid,
                    {
                        ...configToUse,
                        redactions: newRedactions,
                        uploadMethod: 'png' // redactions always force PNG
                    },
                    'select'
                );
            }
            setIsRedacting(false);
            setHasRedactionSelected(false);
            setHasRedactions(false);
        } else {
            setIsRedacting(true);
        }
    };

    const handleCheckboxSelect = (series, isChecked) => {
        if (isChecked) {
            const meta = seriesMetadataMap[series.seriesInstanceUid];
            const trueFrameCount = meta?.totalFrameCount ?? series.instanceCount;
            if (trueFrameCount > 100) return;
            shouldAutoPreview.current = false;
            setActiveSeries(series);
            onSeriesUpdate(series.seriesInstanceUid, {
                start: 1, end: trueFrameCount, step: 1, total: trueFrameCount,
                redactions: []
                // uploadMethod intentionally omitted — global case-level setting applies
            }, 'select');
        } else {
            onSeriesUpdate(series.seriesInstanceUid, null, 'deselect');
        }
    };

    const handleConfigChange = (e) => {
        const { name, value } = e.target;
        let val = parseInt(value, 10);
        if (isNaN(val) || val < 1) val = 1;

        if (activeSeries && (name === 'start' || name === 'end')) {
            const max = effectiveFrameCount;
            const safeVal = Math.min(val, max);
            setCurrentFrameIndex(Math.max(0, safeVal - 1));
        }

        setSliceConfig(prev => {
            const newConfig = { ...prev, [name]: val };
            if (activeSeries) {
                if (name === 'start' && val > prev.end) newConfig.end = val;
                if (name === 'end' && val > effectiveFrameCount) newConfig.end = effectiveFrameCount;
                if (name === 'end' && val < prev.start) newConfig.start = val;
            }
            return newConfig;
        });
    };

    const handleViewerSliceChange = (newIndex) => { setCurrentFrameIndex(newIndex); };

    // Manual Trigger (only if auto-load fails/not found)
    const handlePreviewRequest = async () => {
        if (!activeSeries) return;
        setPreviewJob({ status: 'loading', serverStatus: 'Requesting...', jobId: null, error: null });
        setPreviewImageIds([]);
        setIsRedacting(false);

        try {
            const response = await fetch('/api/dicom/preview', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    studyInstanceUid: activeSeries.studyInstanceUid,
                    seriesInstanceUid: activeSeries.seriesInstanceUid,
                })
            });
            if (!response.ok) throw new Error("Failed to initiate preview");
            const result = await response.json();

            if (result.status === 'Ready') {
                try {
                    const data = await fetchMetadataAndBuildImageIds(activeSeries.seriesInstanceUid);
                    if (!data) throw new Error("Failed to fetch image list");
                    setSeriesMetadataMap(prev => ({ ...prev, [activeSeries.seriesInstanceUid]: data.metadata }));
                    setPreviewImageIds(data.ids);
                    setPreviewJob({ status: 'ready', serverStatus: 'Ready', jobId: null, error: null });
                    const savedState = selectedSeriesMap[activeSeries.seriesInstanceUid];
                    autoUpdateFrameCount(data.metadata, activeSeries, savedState);
                } catch {
                    setPreviewJob({ status: 'error', jobId: null, error: "Failed to load cached images" });
                }
            } else {
                setPreviewJob({ status: 'loading', serverStatus: result.status, jobId: result.jobId, error: null });
            }
        } catch (error) {
            console.error(error);
            setPreviewJob({ status: 'error', jobId: null, error: "Network Error" });
        }
    };

    const handleTimelinePointerDown = (e, type) => {
        e.stopPropagation();
        e.preventDefault();
        isDragging.current = type;
        if (type === 'track' && activeSeries) {
            updateScrubberFromPointer(e.clientX);
            isDragging.current = 'scrubber';
        }
        document.addEventListener('pointermove', handleTimelinePointerMove);
        document.addEventListener('pointerup', handleTimelinePointerUp);
    };

    const updateScrubberFromPointer = (clientX) => {
        if (!timelineRef.current || !activeSeries) return;
        const rect = timelineRef.current.getBoundingClientRect();
        const pct = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
        const total = effectiveFrameCount;
        const newIndex = Math.floor(pct * (total - 1));
        setCurrentFrameIndex(newIndex);
    };

    const handleTimelinePointerMove = (e) => {
        if (!isDragging.current || !timelineRef.current || !activeSeries) return;

        const rect = timelineRef.current.getBoundingClientRect();
        const pct = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
        const total = effectiveFrameCount;
        const val = Math.floor(pct * total) + 1;

        if (isDragging.current === 'scrubber') {
            updateScrubberFromPointer(e.clientX);
        } else if (isDragging.current === 'start') {
            const newStart = Math.min(val, sliceConfig.end);
            const finalStart = Math.max(1, newStart);
            setSliceConfig(prev => ({ ...prev, start: finalStart }));
            setCurrentFrameIndex(finalStart - 1);
        } else if (isDragging.current === 'end') {
            const newEnd = Math.max(val, sliceConfig.start);
            const finalEnd = Math.min(total, newEnd);
            setSliceConfig(prev => ({ ...prev, end: finalEnd }));
            setCurrentFrameIndex(finalEnd - 1);
        }
    };

    const handleTimelinePointerUp = () => {
        isDragging.current = null;
        document.removeEventListener('pointermove', handleTimelinePointerMove);
        document.removeEventListener('pointerup', handleTimelinePointerUp);
    };

    const currentSliceCount = activeSeries
        ? Math.floor((sliceConfig.end - sliceConfig.start) / sliceConfig.step) + 1
        : 0;
    const isLocked = previewJob.status === 'loading';
    const isAlreadySelected = activeSeries && !!selectedSeriesMap[activeSeries.seriesInstanceUid];
    const isSingleImage = effectiveFrameCount <= 1;

    // ── Upload method enforcement ────────────────────────────────────────────────────────
    // savedRedactions: whether this series already has redactions committed
    const savedRedactions = activeSeries
        ? (selectedSeriesMap[activeSeries.seriesInstanceUid]?.redactions ?? [])
        : [];
    const hasCommittedRedactions = savedRedactions.length > 0;

    // hasMultiframe comes from the metadata cache loaded when the series is previewed
    const activeHasMultiframe = activeMetadata?.hasMultiframe ?? false;

    // Is the user culling frames (not the full series)?
    const totalFrameCount = effectiveFrameCount;
    const isCulling = activeSeries
        ? (sliceConfig.start > 1 || sliceConfig.end < totalFrameCount || sliceConfig.step > 1)
        : false;

    // DICOM is not available if:
    //   1. Redactions have been committed (PHI could be in pixel data)
    //   2. Multiframe series + culling (partial frame extraction is not supported)
    const dicomUnavailableReason =
        hasCommittedRedactions ? 'Redactions applied — raw DICOM would bypass pixel-level PHI removal' :
        (activeHasMultiframe && isCulling) ? 'Multiframe series with frame culling — cannot extract partial frames from DICOM' :
        null;

    const dicomAvailable = !dicomUnavailableReason;

    const hasChanges = activeSeries ? (isAlreadySelected
        ? (sliceConfig.start !== selectedSeriesMap[activeSeries.seriesInstanceUid].start ||
            sliceConfig.end !== selectedSeriesMap[activeSeries.seriesInstanceUid].end ||
            sliceConfig.step !== selectedSeriesMap[activeSeries.seriesInstanceUid].step)
        : (sliceConfig.start !== 1 || sliceConfig.end !== effectiveFrameCount || sliceConfig.step !== 1)
    ) : false;

    const canSubmitSubset = activeSeries && currentSliceCount > 0 && currentSliceCount <= 100;
    const isSubmitEnabled = canSubmitSubset && !isLocked && hasChanges;

    return (
        <div className="h-[750px] border border-slate-200 dark:border-slate-700 rounded-lg flex overflow-hidden bg-white dark:bg-slate-800">
            {/* LEFT: Series List */}
            <div className={`w-1/2 border-r border-slate-200 dark:border-slate-700 flex flex-col ${isLocked ? 'opacity-50 pointer-events-none' : ''}`}>
                <div className="p-3 border-b border-slate-200 dark:border-slate-700 font-bold text-xs uppercase text-slate-500 bg-slate-50 dark:bg-slate-900/50">
                    Series List ({seriesList.length})
                </div>
                <div className="flex-1 overflow-y-auto bg-slate-50 dark:bg-slate-900/30">
                    {loading && <div className="p-4 text-center text-slate-400">Loading series...</div>}
                    {!loading && seriesList.length === 0 && <div className="p-4 text-center text-slate-400">No series found.</div>}

                    <ul className="divide-y divide-slate-200 dark:divide-slate-700">
                        {seriesList.map(series => {
                            const isViewed = activeSeries?.seriesInstanceUid === series.seriesInstanceUid;
                            const savedConfig = selectedSeriesMap[series.seriesInstanceUid];
                            const isSelected = !!savedConfig;
                            const finalCount = savedConfig
                                ? Math.floor((savedConfig.end - savedConfig.start) / savedConfig.step) + 1
                                : series.instanceCount;

                            // Per-series metadata from the cache
                            const meta = seriesMetadataMap[series.seriesInstanceUid];

                            return (
                                <li
                                    key={series.seriesInstanceUid}
                                    onClick={() => {
                                        if (!isLocked) {
                                            shouldAutoPreview.current = true;
                                            setActiveSeries(series);
                                        }
                                    }}
                                    className={`cursor-pointer p-3 hover:bg-white dark:hover:bg-slate-800 transition-colors flex items-start gap-3
                                        ${isViewed ? 'bg-white dark:bg-slate-800 border-l-4 border-indigo-500' : 'border-l-4 border-transparent'}`}
                                >
                                    <div className="pt-1" onClick={(e) => e.stopPropagation()}>
                                        {(() => {
                                            const trueCount = meta?.totalFrameCount ?? series.instanceCount;
                                            const needsPreview = !meta && MULTIFRAME_MODALITIES.has(series.modality?.toUpperCase());
                                            const tooMany = trueCount > 100;
                                            const isDisabled = tooMany || isLocked || needsPreview;
                                            const title = needsPreview
                                                ? 'Preview this series first to determine frame count'
                                                : tooMany ? `Too many frames (${trueCount} > 100)` : undefined;
                                            return (
                                                <input
                                                    type="checkbox"
                                                    checked={isSelected}
                                                    disabled={isDisabled}
                                                    title={title}
                                                    onChange={(e) => handleCheckboxSelect(series, e.target.checked)}
                                                    className="h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500 disabled:opacity-50"
                                                />
                                            );
                                        })()}
                                    </div>
                                    <div className="flex-1 min-w-0">
                                        <div className="flex justify-between items-start">
                                            <span className="text-sm font-medium text-slate-800 dark:text-slate-200 truncate pr-2">
                                                {series.seriesDescription || "No Description"}
                                            </span>
                                        </div>
                                        <div className="flex justify-between items-end mt-1">
                                            <div className="flex items-center gap-2">
                                                <span className="text-xs bg-slate-200 dark:bg-slate-700 px-1.5 py-0.5 rounded text-slate-600 dark:text-slate-300 font-mono">
                                                    {series.modality}
                                                </span>
                                                {/* Multiframe badge (persists once metadata is loaded) */}
                                                {meta?.hasMultiframe && (
                                                    <span className="text-[10px] bg-amber-100 dark:bg-amber-900/50 text-amber-700 dark:text-amber-300 px-1.5 py-0.5 rounded font-bold">
                                                        MULTI
                                                    </span>
                                                )}
                                            </div>
                                            <span className="text-xs text-slate-500">
                                                {isSelected
                                                    ? <span className="font-bold text-green-600">{finalCount}/{meta ? meta.totalFrameCount : series.instanceCount} frames</span>
                                                    : (meta
                                                        ? `${meta.totalFrameCount} frames / ${series.instanceCount} img`
                                                        : `${series.instanceCount} img`)
                                                }
                                            </span>
                                        </div>
                                    </div>
                                </li>
                            );
                        })}
                    </ul>
                </div>
            </div>

            {/* RIGHT: Viewer & Controls */}
            <div className="w-1/2 bg-white dark:bg-slate-800 flex flex-col relative">
                {activeSeries ? (
                    <>
                        {/* 1. Viewer Area */}
                        <div className="flex-1 bg-black flex items-center justify-center relative overflow-hidden">
                            {previewJob.status === 'ready' && previewImageIds.length > 0 ? (
                                <DicomViewer
                                    ref={viewerRef}
                                    imageIds={previewImageIds}
                                    viewMode={isRedacting ? 'redact' : 'view'}
                                    activeSlice={currentFrameIndex}
                                    onSliceChange={handleViewerSliceChange}
                                    sliceRange={{ start: sliceConfig.start - 1, end: sliceConfig.end - 1 }}
                                    redactions={restoredRedactions}
                                    onSelectionChange={setHasRedactionSelected}
                                    onRedactionsChange={setHasRedactions}
                                />
                            ) : (
                                <div className="text-center w-64">
                                    {previewJob.status === 'loading' ? (
                                        <div className="flex flex-col items-center">
                                            {/* Spinner matching DicomViewer */}
                                            <div className="w-12 h-12 border-4 border-indigo-500/30 border-t-indigo-500 rounded-full animate-spin mb-4" />

                                            <span className="text-indigo-400 text-sm font-bold uppercase tracking-wider mb-1">Processing</span>
                                            <span className="text-slate-500 text-xs font-mono">
                                                Status: {previewJob.serverStatus || 'Initializing...'}
                                            </span>
                                        </div>
                                    ) : (
                                        <div className="flex flex-col items-center text-slate-500">
                                            {previewJob.error ? (
                                                <div className="text-red-500 mb-2 font-bold text-sm">Error: {previewJob.error}</div>
                                            ) : (
                                                <p className="mb-3 text-sm">Preview not loaded.</p>
                                            )}
                                            <button
                                                onClick={handlePreviewRequest}
                                                className="px-4 py-2 bg-indigo-600 text-white text-sm font-bold rounded hover:bg-indigo-700 transition-colors shadow-sm"
                                            >
                                                {previewJob.error ? 'Retry Preview' : 'Load Images'}
                                            </button>
                                        </div>
                                    )}
                                </div>
                            )}

                            {/* Overlay Info */}
                            <div className="absolute top-2 right-2 bg-black/50 text-white text-xs px-2 py-1 rounded font-mono pointer-events-none z-20">
                                Img: {currentFrameIndex + 1} / {effectiveFrameCount}
                            </div>
                        </div>

                        {/* 2. Timeline Controls */}
                        <div className="h-32 bg-slate-100 dark:bg-slate-900 border-t border-slate-200 dark:border-slate-700 flex flex-col p-3 select-none">
                            <div className="flex items-center mb-3">
                                {/* LEFT: Per-series constraint indicator + Redaction Tool Bar */}
                                <div className="flex items-center gap-2">
                                    {/* Shown only when this series is forced to PNG regardless of the global setting */}
                                    {!dicomAvailable && (
                                        <span
                                            className="text-[10px] text-amber-600 dark:text-amber-400 font-semibold cursor-help"
                                            title={dicomUnavailableReason}
                                        >
                                            ⚠ Uploads as PNG
                                        </span>
                                    )}

                                    {previewJob.status === 'ready' && (
                                        <div className="flex items-center bg-white dark:bg-slate-800 rounded border border-slate-300 dark:border-slate-600 p-0.5 shadow-sm">
                                            <button
                                                onClick={handleRedactionToggle}
                                                className={`px-3 py-1 text-xs font-bold uppercase rounded transition-colors ${isRedacting
                                                    ? 'bg-red-500 text-white shadow-inner shadow-red-700'
                                                    : 'text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700'
                                                    }`}
                                            >
                                                {isRedacting ? 'Save' : 'Redact'}
                                            </button>
                                            {isRedacting && (
                                                <>
                                                    <div className="w-px h-4 bg-slate-300 dark:bg-slate-600 mx-1"></div>
                                                    <button
                                                        onClick={() => viewerRef.current?.deleteSelected()}
                                                        disabled={!hasRedactionSelected}
                                                        className={`px-2 py-1 text-xs rounded transition-colors ${hasRedactionSelected
                                                                ? 'text-slate-500 hover:text-red-500 hover:bg-slate-100 dark:hover:bg-slate-700'
                                                                : 'text-slate-300 dark:text-slate-600 cursor-not-allowed'
                                                            }`}
                                                        title="Delete selected box"
                                                    >
                                                        Delete
                                                    </button>
                                                    <div className="w-px h-4 bg-slate-300 dark:bg-slate-600 mx-1"></div>
                                                    <button
                                                        onClick={() => viewerRef.current?.clearRedactions()}
                                                        disabled={!hasRedactions}
                                                        className={`px-2 py-1 text-xs rounded transition-colors ${hasRedactions
                                                                ? 'text-slate-500 hover:text-red-500 hover:bg-slate-100 dark:hover:bg-slate-700'
                                                                : 'text-slate-300 dark:text-slate-600 cursor-not-allowed'
                                                            }`}
                                                    >
                                                        Clear
                                                    </button>
                                                </>
                                            )}
                                        </div>
                                    )}
                                </div>

                                {/* RIGHT: Image Step Selector + Subset Controls */}
                                <div className="ml-auto flex items-center gap-3">
                                    <div className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-400">
                                        <span>Select every</span>
                                        <input
                                            type="number" name="step" value={sliceConfig.step} onChange={handleConfigChange}
                                            disabled={isSingleImage}
                                            className={`w-12 text-center text-xs p-1 rounded border border-slate-300 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200 ${isSingleImage ? 'opacity-50 cursor-not-allowed' : ''}`}
                                        />
                                        <span>image.</span>
                                    </div>
                                    <div className="w-px h-4 bg-slate-300 dark:bg-slate-600"></div>
                                    <span className={`text-xs ${currentSliceCount > 100 ? 'text-red-500 font-bold' : 'text-slate-500'}`}>
                                        Selection: {currentSliceCount} images
                                    </span>
                                    {/* SUBSET BUTTON */}
                                    <button
                                        onClick={handleSubsetSave}
                                        disabled={!isSubmitEnabled}
                                        className={`px-4 py-1.5 rounded text-xs uppercase tracking-wide transition-all ${isSubmitEnabled ? 'bg-indigo-600 text-white hover:bg-indigo-700' : 'bg-slate-200 text-slate-400 cursor-not-allowed'
                                            }`}
                                    >
                                        {isAlreadySelected ? 'Update Subset' : 'Select Subset'}
                                    </button>
                                </div>
                            </div>

                            <div className="flex items-center gap-4 flex-1">
                                <div className="flex flex-col items-center">
                                    <label className="text-[9px] uppercase font-bold text-slate-400">Start</label>
                                    <input
                                        type="number" name="start" value={sliceConfig.start} onChange={handleConfigChange}
                                        disabled={isSingleImage}
                                        className={`w-14 text-center text-sm font-mono p-1 rounded border-slate-300 ${isSingleImage ? 'opacity-50 bg-slate-100 text-slate-400 cursor-not-allowed' : ''}`}
                                    />
                                </div>

                                {/* Timeline Track */}
                                <div
                                    className="flex-1 h-12 relative cursor-pointer group"
                                    onPointerDown={(e) => handleTimelinePointerDown(e, 'track')}
                                    ref={timelineRef}
                                >
                                    {/* Track Background */}
                                    <div className="absolute top-1/2 left-0 right-0 h-2 bg-slate-300 dark:bg-slate-700 rounded-full -mt-1 overflow-hidden">
                                        <div
                                            className="absolute top-0 bottom-0 bg-indigo-500/50"
                                            style={{
                                                left: `${((sliceConfig.start - 1) / effectiveFrameCount) * 100}%`,
                                                right: `${100 - ((sliceConfig.end) / effectiveFrameCount) * 100}%`
                                            }}
                                        />
                                    </div>
                                    {/* Start Handle */}
                                    <div
                                        className={`absolute top-1/2 w-4 h-8 rounded shadow-md z-10 flex items-center justify-center -mt-4 -ml-2 transition-transform
                                            ${isSingleImage ? 'bg-slate-400 cursor-not-allowed' : 'bg-indigo-600 cursor-ew-resize hover:scale-110'}`}
                                        style={{ left: `${((sliceConfig.start - 1) / effectiveFrameCount) * 100}%` }}
                                        onPointerDown={(e) => !isSingleImage && handleTimelinePointerDown(e, 'start')}
                                    >
                                        <div className="w-0.5 h-4 bg-white/50 rounded-full" />
                                    </div>
                                    {/* End Handle */}
                                    <div
                                        className={`absolute top-1/2 w-4 h-8 rounded shadow-md z-10 flex items-center justify-center -mt-4 -ml-2 transition-transform
                                            ${isSingleImage ? 'bg-slate-400 cursor-not-allowed' : 'bg-indigo-600 cursor-ew-resize hover:scale-110'}`}
                                        style={{ left: `${(sliceConfig.end / effectiveFrameCount) * 100}%` }}
                                        onPointerDown={(e) => !isSingleImage && handleTimelinePointerDown(e, 'end')}
                                    >
                                        <div className="w-0.5 h-4 bg-white/50 rounded-full" />
                                    </div>
                                    {/* Scrubber */}
                                    <div
                                        className="absolute top-0 bottom-0 w-0.5 bg-white border-x border-red-500 z-20 pointer-events-none transition-all duration-75"
                                        style={{ left: `${(currentFrameIndex / (effectiveFrameCount - 1 || 1)) * 100}%` }}
                                    >
                                        <div className="absolute -top-1 -left-1.5 w-3 h-3 bg-red-500 rounded-full shadow-sm" />
                                    </div>
                                </div>

                                <div className="flex flex-col items-center">
                                    <label className="text-[9px] uppercase font-bold text-slate-400">End</label>
                                    <input
                                        type="number" name="end" value={sliceConfig.end} onChange={handleConfigChange}
                                        disabled={isSingleImage}
                                        className={`w-14 text-center text-sm font-mono p-1 rounded border-slate-300 ${isSingleImage ? 'opacity-50 bg-slate-100 text-slate-400 cursor-not-allowed' : ''}`}
                                    />
                                </div>
                            </div>
                        </div>
                    </>
                ) : (
                    <div className="flex-1 flex flex-col items-center justify-center text-slate-400 p-8 text-center">
                        <svg className="w-12 h-12 mb-3 text-slate-300" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 6h16M4 10h16M4 14h16M4 18h16" /></svg>
                        <p className="text-sm">Select a series to begin.</p>
                    </div>
                )}
            </div>
        </div>

    );
};

export default SeriesPicker;
