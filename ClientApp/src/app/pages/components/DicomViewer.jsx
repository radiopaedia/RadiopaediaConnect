import { useEffect, useRef, useState, useImperativeHandle, forwardRef } from 'react';
import Hammer from 'hammerjs';
import dicomParser from 'dicom-parser';
import * as cornerstone from 'cornerstone-core';
import * as cornerstoneMath from 'cornerstone-math';
import * as cornerstoneTools from 'cornerstone-tools';
import * as cornerstoneWADOImageLoader from 'cornerstone-wado-image-loader';

// Ensure external dependencies are linked
cornerstoneTools.external.cornerstone = cornerstone;
cornerstoneTools.external.cornerstoneMath = cornerstoneMath;
cornerstoneTools.external.Hammer = Hammer;
cornerstoneWADOImageLoader.external.cornerstone = cornerstone;
cornerstoneWADOImageLoader.external.dicomParser = dicomParser;

// ensure tools init only runs once per session
let isToolsInitialized = false;

const DicomViewer = forwardRef(({
    imageIds,
    viewMode = 'view', // 'view' | 'redact'
    activeSlice = 0,
    onSliceChange,
    sliceRange = null,
    redactions: propRedactions = [],
    onSelectionChange,   // Called with (hasSelection: bool) when selectedId changes
    onRedactionsChange   // Called with (hasRedactions: bool) when the redactions list changes
}, ref) => {
    const elementRef = useRef(null);
    const [isLoading, setIsLoading] = useState(true);

    const [redactions, setRedactions] = useState([]);
    const [selectedId, setSelectedId] = useState(null);

    // This restores the drawings when the user switches back to this series
    useEffect(() => {
        if (propRedactions) {
            const loaded = propRedactions.map((r, i) => ({
                id: r.id || `imported-${Date.now()}-${i}`, // Ensure ID exists for internal logic
                x: r.x,
                y: r.y,
                w: r.w,
                h: r.h
            }));
            setRedactions(loaded);
        }
    }, [propRedactions]);

    // Notify parent when a redaction box is selected/deselected
    useEffect(() => {
        if (onSelectionChange) onSelectionChange(selectedId !== null);
    }, [selectedId, onSelectionChange]);

    // Notify parent when the redactions list becomes empty or non-empty
    useEffect(() => {
        if (onRedactionsChange) onRedactionsChange(redactions.length > 0);
    }, [redactions.length, onRedactionsChange]);

    // Interaction State
    const [interaction, setInteraction] = useState({
        mode: 'idle',
        activeId: null,
        startCoords: null,
        initialRect: null,
        handle: null
    });

    const isProgrammaticScroll = useRef(false);
    const isManualScrolling = useRef(false);
    const manualScrollTimeout = useRef(null);
    const rangeRef = useRef(sliceRange);

    useEffect(() => { rangeRef.current = sliceRange; }, [sliceRange]);

    useImperativeHandle(ref, () => ({
        getRedactionData: () => {
            // Return clean data (x, y, w, h) without internal IDs
            return redactions.map(r => ({ x: r.x, y: r.y, w: r.w, h: r.h }));
        },
        clearRedactions: () => {
            setRedactions([]);
            setSelectedId(null);
        },
        deleteSelected: () => {
            if (selectedId) {
                setRedactions(prev => prev.filter(r => r.id !== selectedId));
                setSelectedId(null);
            }
        },
        resize: () => {
            if (elementRef.current) cornerstone.resize(elementRef.current);
        }
    }));

    useEffect(() => {
        const handleKeyDown = (e) => {
            if (viewMode === 'redact' && selectedId && (e.key === 'Delete' || e.key === 'Backspace')) {
                setRedactions(prev => prev.filter(r => r.id !== selectedId));
                setSelectedId(null);
            }
        };
        window.addEventListener('keydown', handleKeyDown);
        return () => window.removeEventListener('keydown', handleKeyDown);
    }, [viewMode, selectedId]);

    useEffect(() => {
        const element = elementRef.current;
        if (!element) return;

        if (!isToolsInitialized) {
            cornerstoneTools.init({
                showSVGCursors: true,
                globalToolSyncEnabled: false
            });
            isToolsInitialized = true;
        }

        const enabledElements = cornerstone.getEnabledElements();
        const isEnabled = enabledElements.some(e => e.element === element);

        if (!isEnabled) {
            cornerstone.enable(element);
        }

        const WwwcTool = cornerstoneTools.WwwcTool;
        const StackScrollMouseWheelTool = cornerstoneTools.StackScrollMouseWheelTool;

        cornerstoneTools.addToolForElement(element, WwwcTool);
        cornerstoneTools.addToolForElement(element, StackScrollMouseWheelTool);

        const scrollTool = cornerstoneTools.getToolForElement(element, 'StackScrollMouseWheel');
        if (scrollTool) {
            scrollTool.configuration = { loop: false, allowSkipping: true, invert: false };
        }

        return () => {
            const isStillEnabled = cornerstone.getEnabledElements().some(e => e.element === element);
            if (isStillEnabled) {
                try {
                    cornerstone.disable(element);
                } catch (e) {
                    console.warn("Cornerstone cleanup warning:", e);
                }
            }
        };
    }, []);

    useEffect(() => {
        const element = elementRef.current;
        if (!element || !imageIds || imageIds.length === 0) return;

        setIsLoading(true);

        const initialIndex = (sliceRange && (activeSlice < sliceRange.start || activeSlice > sliceRange.end))
            ? sliceRange.start : activeSlice;

        const stack = { currentImageIdIndex: initialIndex, imageIds: imageIds };

        cornerstone.loadAndCacheImage(imageIds[initialIndex])
            .then((image) => {
                try {
                    cornerstone.displayImage(element, image);

                    // force resize to fix blank canvas on first render
                    cornerstone.resize(element);

                    cornerstoneTools.clearToolState(element, 'stack');
                    cornerstoneTools.addStackStateManager(element, ['stack']);
                    cornerstoneTools.addToolState(element, 'stack', stack);

                    updateToolModes(viewMode);
                    setIsLoading(false);
                } catch (err) {
                    console.error("Display Error:", err);
                    setIsLoading(false);
                }
            })
            .catch(err => {
                console.error("Error loading initial image:", err);
                setIsLoading(false);
            });

        const onNewImage = (e) => {
            if (isProgrammaticScroll.current) return;

            isManualScrolling.current = true;
            if (manualScrollTimeout.current) clearTimeout(manualScrollTimeout.current);
            manualScrollTimeout.current = setTimeout(() => {
                isManualScrolling.current = false;
            }, 250);

            let newIndex = e.detail.newImageIdIndex;
            if (newIndex === undefined && elementRef.current) {
                const stackState = cornerstoneTools.getToolState(elementRef.current, 'stack');
                if (stackState?.data?.length) newIndex = stackState.data[0].currentImageIdIndex;
            }

            if (newIndex !== undefined && onSliceChange) onSliceChange(newIndex);
        };

        const onStackScroll = (e) => {
            const newIndex = e.detail.newImageIdIndex;
            const currentRange = rangeRef.current;
            if (currentRange && (newIndex < currentRange.start || newIndex > currentRange.end)) {
                e.preventDefault();
                e.stopPropagation();
            }
        };

        element.addEventListener('cornerstonenewimage', onNewImage);
        element.addEventListener('cornerstonestackscroll', onStackScroll);

        return () => {
            element.removeEventListener('cornerstonenewimage', onNewImage);
            element.removeEventListener('cornerstonestackscroll', onStackScroll);
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [imageIds]);

    useEffect(() => {
        const element = elementRef.current;
        if (!element || isLoading) return;

        const stackState = cornerstoneTools.getToolState(element, 'stack');
        if (stackState?.data?.length) {
            const currentIndex = stackState.data[0].currentImageIdIndex;
            if (currentIndex !== activeSlice && !isManualScrolling.current) {
                const stackData = stackState.data[0];
                let index = activeSlice;
                if (index < 0) index = 0;
                if (index >= stackData.imageIds.length) index = stackData.imageIds.length - 1;

                isProgrammaticScroll.current = true;
                stackData.currentImageIdIndex = index;

                cornerstone.loadAndCacheImage(stackData.imageIds[index]).then((image) => {
                    try { cornerstone.displayImage(element, image); } catch { /* safe */ }
                    setTimeout(() => { isProgrammaticScroll.current = false; }, 0);
                });
            }
        }
    }, [activeSlice, isLoading]);

    useEffect(() => {
        updateToolModes(viewMode);
    }, [viewMode, isLoading]);

    const updateToolModes = (mode) => {
        const element = elementRef.current;
        if (!element) return;

        try {
            cornerstoneTools.setToolActiveForElement(element, 'StackScrollMouseWheel', {});

            if (mode === 'redact') {
                cornerstoneTools.setToolPassiveForElement(element, 'Wwwc');
            } else {
                cornerstoneTools.setToolActiveForElement(element, 'Wwwc', { mouseButtonMask: 1 });
                setSelectedId(null);
            }
        } catch (e) {
            console.warn("Tool update skipped:", e);
        }
    };

    const getCoords = (e) => {
        const element = elementRef.current;
        if (!element) return { x: 0, y: 0 };
        try {
            return cornerstone.pageToPixel(element, e.pageX, e.pageY);
        } catch {
            return { x: 0, y: 0 };
        }
    };

    const getStyle = (rect) => {
        const element = elementRef.current;
        if (!element || isLoading) return { display: 'none' };

        try {
            const tl = cornerstone.pixelToCanvas(element, { x: rect.x, y: rect.y });
            const br = cornerstone.pixelToCanvas(element, { x: rect.x + rect.w, y: rect.y + rect.h });
            return {
                x: Math.min(tl.x, br.x),
                y: Math.min(tl.y, br.y),
                width: Math.abs(br.x - tl.x),
                height: Math.abs(br.y - tl.y)
            };
        } catch {
            return { display: 'none' };
        }
    };

    const handleMouseDown = (e, type, id = null, handle = null) => {
        if (viewMode !== 'redact' || e.button !== 0) return;

        e.preventDefault();
        e.stopPropagation();

        const mouseImgCoords = getCoords(e);

        if (type === 'bg') {
            setSelectedId(null);
            setInteraction({ mode: 'drawing', startCoords: mouseImgCoords, activeId: null });
        } else if (type === 'rect') {
            setSelectedId(id);
            const rect = redactions.find(r => r.id === id);
            setInteraction({ mode: 'moving', activeId: id, startCoords: mouseImgCoords, initialRect: { ...rect } });
        } else if (type === 'handle') {
            const rect = redactions.find(r => r.id === id);
            setInteraction({ mode: 'resizing', activeId: id, startCoords: mouseImgCoords, initialRect: { ...rect }, handle });
        }
    };

    const handleMouseMove = (e) => {
        if (interaction.mode === 'idle') return;
        e.preventDefault();

        const currentCoords = getCoords(e);
        const deltaX = currentCoords.x - interaction.startCoords.x;
        const deltaY = currentCoords.y - interaction.startCoords.y;

        if (interaction.mode === 'drawing') {
            setInteraction(prev => ({ ...prev, currentCoords }));
        }
        else if (interaction.mode === 'moving') {
            setRedactions(prev => prev.map(r => {
                if (r.id !== interaction.activeId) return r;
                return {
                    ...r,
                    x: interaction.initialRect.x + deltaX,
                    y: interaction.initialRect.y + deltaY
                };
            }));
        }
        else if (interaction.mode === 'resizing') {
            const { initialRect, handle } = interaction;
            let newRect = { ...initialRect };

            if (handle.includes('e')) newRect.w = Math.max(5, initialRect.w + deltaX);
            if (handle.includes('s')) newRect.h = Math.max(5, initialRect.h + deltaY);
            if (handle.includes('w')) {
                const newW = Math.max(5, initialRect.w - deltaX);
                newRect.x = initialRect.x + (initialRect.w - newW);
                newRect.w = newW;
            }
            if (handle.includes('n')) {
                const newH = Math.max(5, initialRect.h - deltaY);
                newRect.y = initialRect.y + (initialRect.h - newH);
                newRect.h = newH;
            }

            setRedactions(prev => prev.map(r => r.id === interaction.activeId ? newRect : r));
        }
    };

    const handleMouseUp = () => {
        if (interaction.mode === 'drawing') {
            const start = interaction.startCoords;
            const current = interaction.currentCoords || start;

            const x = Math.min(start.x, current.x);
            const y = Math.min(start.y, current.y);
            const w = Math.abs(start.x - current.x);
            const h = Math.abs(start.y - current.y);

            if (w > 5 && h > 5) {
                const newId = Date.now();
                setRedactions(prev => [...prev, { id: newId, x, y, w, h }]);
                setSelectedId(newId);
            }
        }
        setInteraction({ mode: 'idle', activeId: null });
    };

    return (
        <div
            className="w-full h-full bg-black relative group overflow-hidden select-none"
            onMouseMove={handleMouseMove}
            onMouseUp={handleMouseUp}
            onMouseLeave={handleMouseUp}
            onContextMenu={(e) => e.preventDefault()}
        >
            <div ref={elementRef} className="w-full h-full outline-none block" />

            <svg
                className={`absolute inset-0 w-full h-full z-10 ${viewMode === 'redact' ? 'pointer-events-auto cursor-crosshair' : 'pointer-events-none cursor-default'}`}
                onMouseDown={(e) => handleMouseDown(e, 'bg')}
            >
                {redactions.map((r) => {
                    const style = getStyle(r);
                    const isSelected = r.id === selectedId;

                    return (
                        <g key={r.id} style={{ pointerEvents: viewMode === 'redact' ? 'auto' : 'none' }}>
                            <rect
                                x={style.x} y={style.y} width={style.width} height={style.height}
                                fill={isSelected ? "rgba(220, 38, 38, 0.2)" : "black"}
                                stroke={viewMode === 'redact' ? (isSelected ? "#fff" : "red") : "transparent"}
                                strokeWidth={isSelected ? 2 : 1}
                                strokeDasharray={isSelected ? "4" : ""}
                                opacity={viewMode === 'redact' ? 0.9 : 1.0}
                                onMouseDown={(e) => handleMouseDown(e, 'rect', r.id)}
                                style={{ cursor: viewMode === 'redact' ? 'move' : 'default' }}
                            />

                            {isSelected && viewMode === 'redact' && (
                                ['nw', 'ne', 'sw', 'se'].map(h => {
                                    const hx = h.includes('w') ? style.x - 4 : style.x + style.width - 4;
                                    const hy = h.includes('n') ? style.y - 4 : style.y + style.height - 4;
                                    return (
                                        <rect
                                            key={h} x={hx} y={hy} width={8} height={8}
                                            fill="white" stroke="red" strokeWidth={1}
                                            style={{ cursor: `${h}-resize` }}
                                            onMouseDown={(e) => handleMouseDown(e, 'handle', r.id, h)}
                                        />
                                    );
                                })
                            )}
                        </g>
                    );
                })}

                {interaction.mode === 'drawing' && interaction.currentCoords && (() => {
                    const x = Math.min(interaction.startCoords.x, interaction.currentCoords.x);
                    const y = Math.min(interaction.startCoords.y, interaction.currentCoords.y);
                    const w = Math.abs(interaction.startCoords.x - interaction.currentCoords.x);
                    const h = Math.abs(interaction.startCoords.y - interaction.currentCoords.y);
                    const style = getStyle({ x, y, w, h });
                    return (
                        <rect
                            {...style} fill="rgba(255, 0, 0, 0.1)" stroke="red" strokeWidth="2" strokeDasharray="4"
                            style={{ pointerEvents: 'none' }}
                        />
                    );
                })()}
            </svg>

            {isLoading && (
                <div className="absolute inset-0 bg-black/80 z-20 flex flex-col items-center justify-center">
                    <div className="w-8 h-8 border-4 border-indigo-500/30 border-t-indigo-500 rounded-full animate-spin mb-2" />
                    <span className="text-indigo-400 text-xs font-medium">Loading DICOM...</span>
                </div>
            )}
        </div>
    );
});

DicomViewer.displayName = 'DicomViewer';
export default DicomViewer;