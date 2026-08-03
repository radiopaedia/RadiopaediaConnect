import { useState, useRef, useEffect } from 'react';
import MainLayout from './MainLayout';
import PacsSearchForm from './components/PacsSearchForm';
import CaseDetailsForm from './components/CaseDetailsForm';
import SeriesPicker from './components/SeriesPicker';
import StudyList from './components/StudyList';
import CaseDetailDrawer from './CaseDetailDrawer';
import DuplicatePatientWarningModal from './DuplicatePatientWarningModal';
import SubmissionSuccessModal from './SubmissionSuccessModal';
import AnonymisationDrawer from './components/AnonymisationDrawer';

const SYSTEM_MAP = {
    "Breast": 1, "Vascular": 2, "Central Nervous System": 3, "Chest": 4,
    "Gastrointestinal": 6, "Head & Neck": 7, "Hepatobiliary": 8, "Musculoskeletal": 9,
    "Urogenital": 11, "Paediatrics": 12, "Spine": 15, "Cardiac": 16,
    "Interventional": 17, "Obstetrics": 18, "Gynaecology": 19, "Haematology": 20,
    "Forensic": 21, "Oncology": 22, "Trauma": 23, "Not Applicable": 24
};

const DIAGNOSTIC_CERTAINTY_OPTIONS = [
    { id: 5, label: "Not applicable" },
    { id: 1, label: "Possible" },
    { id: 2, label: "Probable" },
    { id: 3, label: "Almost Certain" },
    { id: 4, label: "Certain" }
];

const DashboardPage = ({ user, onLogout }) => {
    const [viewMode, setViewMode] = useState('search');

    const [studies, setStudies] = useState([]);
    const [isSearching, setIsSearching] = useState(false);
    const [hasSearched, setHasSearched] = useState(false);
    const [selectedSearchStudy, setSelectedSearchStudy] = useState(null);
    const [isInitializingDraft, setIsInitializingDraft] = useState(false);

    const [patientInfo, setPatientInfo] = useState(null);
    const [patientStudies, setPatientStudies] = useState([]);
    const [errors, setErrors] = useState({});

    const [caseData, setCaseData] = useState({
        title: '',
        presentation: '',
        system: '',
        age: '',
        sex: '',
        diagnostic_certainty: '',
    });

    const [caseDiscussion, setCaseDiscussion] = useState('');
    const [draftContent, setDraftContent] = useState({});
    const [activeStudyUid, setActiveStudyUid] = useState(null);
    const [isLoadingSeries, setIsLoadingSeries] = useState(false);
    const [loadedSeriesData, setLoadedSeriesData] = useState({});

    const [showDuplicateWarning, setShowDuplicateWarning] = useState(false);
    const [existingPatientCases, setExistingPatientCases] = useState([]);
    const [pendingDraftStudy, setPendingDraftStudy] = useState(null);

    const [drawerOpen, setDrawerOpen] = useState(false);
    const [selectedCaseId, setSelectedCaseId] = useState(null);

    const [showSuccessModal, setShowSuccessModal] = useState(false);
    const [submittedCaseId, setSubmittedCaseId] = useState(null);

    // Global upload format for this case — 'dicom' (native, anonymised) or 'png' (pixel-only).
    // Individual series may still be forced to 'png' by constraints (redactions, multiframe culling).
    const [uploadMethod, setUploadMethod] = useState('dicom');

    // When set, the draft view appends studies to this existing case instead of
    // creating a new one. Holds the case detail from /api/cases/{id}.
    const [appendTarget, setAppendTarget] = useState(null);

    // Anonymisation details drawer (shared across the case)
    const [anonDrawerOpen, setAnonDrawerOpen] = useState(false);

    const dragItem = useRef();
    const dragOverItem = useRef();

    const moveStudyToSelectedEnd = (studyUid) => {
        setPatientStudies(prev => {
            const index = prev.findIndex(s => s.studyInstanceUid === studyUid);
            if (index < 0) return prev;

            let selectedCount = 0;
            prev.forEach(s => {
                if (s.studyInstanceUid === studyUid) return;
                const selection = draftContent[s.studyInstanceUid]?.seriesSelection || {};
                if (Object.keys(selection).length > 0) selectedCount++;
            });

            if (index === selectedCount) return prev;

            const newStudies = [...prev];
            const [moved] = newStudies.splice(index, 1);
            newStudies.splice(selectedCount, 0, moved);
            return newStudies;
        });
    };

    const handleDragStart = (e, position) => {
        dragItem.current = position;
        e.dataTransfer.effectAllowed = "move";
    };

    const handleDragEnter = (e, position) => {
        dragOverItem.current = position;
    };

    const handleDragEnd = () => {
        const dragIndex = dragItem.current;
        const dragOverIndex = dragOverItem.current;

        if (dragIndex !== undefined && dragOverIndex !== undefined && dragIndex !== dragOverIndex) {
            setPatientStudies(prev => {
                const newStudies = [...prev];
                const [draggedItem] = newStudies.splice(dragIndex, 1);
                newStudies.splice(dragOverIndex, 0, draggedItem);
                return newStudies;
            });
        }
        dragItem.current = null;
        dragOverItem.current = null;
    };

    // Entry point for "add to existing case" — e.g. /?appendTo={caseId} from My Cases
    useEffect(() => {
        const params = new URLSearchParams(window.location.search);
        const appendTo = params.get('appendTo');
        if (appendTo) {
            window.history.replaceState({}, '', '/');
            enterAppendMode(appendTo);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // Loads an existing completed case and opens the draft view in append mode:
    // same study/series pickers, but submission adds to the case on Radiopaedia.
    const enterAppendMode = async (caseId, fallbackNodeName = null) => {
        setIsInitializingDraft(true);
        try {
            const res = await fetch(`/api/cases/${caseId}`);
            if (!res.ok) throw new Error('Failed to load case details');
            const detail = await res.json();

            if (detail.status !== 'Completed' || !detail.radiopaediaCaseId) {
                alert('Studies can only be added to a case that has completed uploading to Radiopaedia.');
                return;
            }
            if (!detail.patientId) {
                alert('This case has no patient ID recorded, so its studies cannot be located on PACS.');
                return;
            }
            const nodeName = detail.studies?.[0]?.remoteNodeName || fallbackNodeName;
            if (!nodeName) {
                alert('Could not determine which PACS node this case was retrieved from.');
                return;
            }

            const response = await fetch('/api/dicom/studies', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    patientId: detail.patientId,
                    remoteNodeName: nodeName,
                    patientName: '',
                    accessionNumber: ''
                })
            });
            if (!response.ok) throw new Error('Failed to retrieve patient studies from PACS');

            const results = await response.json();
            const relatedStudies = results.map(study => ({ ...study, remoteNodeName: nodeName }));
            relatedStudies.sort((a, b) => new Date(b.studyDate) - new Date(a.studyDate));

            setAppendTarget(detail);
            setPatientInfo({
                name: (detail.patientName || '').replace('^', ', '),
                id: detail.patientId,
                dob: detail.patientDob,
                sex: detail.sex
            });
            setPatientStudies(relatedStudies);
            setErrors({});
            setViewMode('draft');

            if (relatedStudies.length > 0) {
                const first = relatedStudies[0];
                initStudyInDraft(first);
                setActiveStudyUid(first.studyInstanceUid);
                fetchSeriesForStudy(first);
            }
        } catch (error) {
            console.error(error);
            alert('Error preparing case for additional studies: ' + error.message);
        } finally {
            setIsInitializingDraft(false);
        }
    };

    // Clears everything to return to a pristine "Search" state
    const resetToSearch = () => {
        setStudies([]);
        setHasSearched(false);
        setSelectedSearchStudy(null);
        setIsSearching(false);
        setIsInitializingDraft(false);

        setPatientInfo(null);
        setPatientStudies([]);
        setErrors({});
        setCaseData({
            title: '', presentation: '', system: '', age: '', sex: '', diagnostic_certainty: ''
        });
        setCaseDiscussion('');
        setDraftContent({});
        setActiveStudyUid(null);
        setLoadedSeriesData({});

        setShowDuplicateWarning(false);
        setExistingPatientCases([]);
        setPendingDraftStudy(null);

        setUploadMethod('dicom');
        setAnonDrawerOpen(false);
        setAppendTarget(null);

        setViewMode('search');
    };

    const handleSearch = async (criteria) => {
        setIsSearching(true);
        setSelectedSearchStudy(null);
        setHasSearched(true);
        try {
            const response = await fetch('/api/dicom/studies', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(criteria)
            });
            const results = await response.json();

            const resultsWithNode = results.map(study => ({
                ...study,
                remoteNodeName: criteria.remoteNodeName
            }));

            setStudies(resultsWithNode);
        } catch (error) {
            console.error(error);
            alert("Search failed.");
        } finally {
            setIsSearching(false);
        }
    };

    const handleInitializeDraft = async () => {
        if (!selectedSearchStudy) return;

        setIsInitializingDraft(true);
        try {
            // Check for existing cases with same patient ID before proceeding
            const existingCases = await checkPatientDuplicates(selectedSearchStudy.patientId);

            if (existingCases.length > 0) {
                // Store the selected study and show warning modal
                setPendingDraftStudy(selectedSearchStudy);
                setExistingPatientCases(existingCases);
                setShowDuplicateWarning(true);
                setIsInitializingDraft(false);
                return;
            }

            // No duplicates found, proceed directly
            await proceedWithDraftInit(selectedSearchStudy);
        } catch (error) {
            console.error(error);
            alert("Error initializing case: " + error.message);
            setIsInitializingDraft(false);
        }
    };

    // called from both the normal flow and the "Continue Anyway" modal action
    const proceedWithDraftInit = async (primaryStudy) => {
        setIsInitializingDraft(true);
        try {
            const criteria = {
                patientId: primaryStudy.patientId,
                remoteNodeName: primaryStudy.remoteNodeName,
                patientName: '',
                accessionNumber: ''
            };

            const response = await fetch('/api/dicom/studies', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(criteria)
            });

            if (!response.ok) throw new Error("Failed to retrieve patient studies");

            const results = await response.json();
            const relatedStudies = results.map(study => ({
                ...study,
                remoteNodeName: criteria.remoteNodeName
            }));

            relatedStudies.sort((a, b) => new Date(b.studyDate) - new Date(a.studyDate));

            let age = primaryStudy.patientAge || '';
            let sex = '';
            if (primaryStudy.patientSex === 'M') sex = 'male';
            if (primaryStudy.patientSex === 'F') sex = 'female';
            if (primaryStudy.patientSex === 'O') sex = 'other';

            setPatientInfo({
                name: primaryStudy.patientName?.replace('^', ', '),
                id: primaryStudy.patientId,
                dob: primaryStudy.patientBirthDate,
                sex: primaryStudy.patientSex
            });

            setCaseData(prev => ({
                ...prev,
                title: primaryStudy.studyDescription || '',
                age: age,
                sex: sex
            }));

            setPatientStudies(relatedStudies);
            setErrors({});

            initStudyInDraft(primaryStudy);
            setViewMode('draft');
            setActiveStudyUid(primaryStudy.studyInstanceUid);
            fetchSeriesForStudy(primaryStudy);

        } catch (error) {
            console.error(error);
            alert("Error initializing case: " + error.message);
        } finally {
            setIsInitializingDraft(false);
        }
    };

    const initStudyInDraft = (study) => {
        setDraftContent(prev => {
            if (prev[study.studyInstanceUid]) return prev;
            return {
                ...prev,
                [study.studyInstanceUid]: {
                    findings: '',
                    seriesSelection: {}
                }
            };
        });
    };

    const fetchSeriesForStudy = async (study) => {
        if (loadedSeriesData[study.studyInstanceUid]) return;

        setIsLoadingSeries(true);
        try {
            const params = new URLSearchParams({
                studyUid: study.studyInstanceUid,
                nodeName: study.remoteNodeName
            });
            const res = await fetch(`/api/dicom/series?${params.toString()}`);
            const data = await res.json();
            setLoadedSeriesData(prev => ({ ...prev, [study.studyInstanceUid]: data }));
        } catch (err) {
            console.error(err);
        } finally {
            setIsLoadingSeries(false);
        }
    };

    const validateForm = () => {
        // Case details are fixed when appending — only the series selection matters
        if (appendTarget) {
            setErrors({});
            return true;
        }

        const newErrors = {};
        if (!caseData.title.trim()) newErrors.title = 'Title is required';
        if (!caseData.system) newErrors.system = 'System is required';
        if (!caseData.presentation.trim()) newErrors.presentation = 'Presentation is required';
        if (!caseData.age.toString().trim()) newErrors.age = 'Age is required';
        if (!caseData.sex) newErrors.sex = 'Sex is required';

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    };

    const handleSwitchActiveStudy = (study) => {
        initStudyInDraft(study);
        setActiveStudyUid(study.studyInstanceUid);
        fetchSeriesForStudy(study);
    };

    const handleCaseChange = (e) => {
        const { name, value, type, checked } = e.target;
        setCaseData(prev => ({
            ...prev,
            [name]: type === 'checkbox' ? checked : value
        }));
        if (errors[name]) setErrors(prev => ({ ...prev, [name]: null }));
    };

    const handleStudyFindingsChange = (e) => {
        setDraftContent(prev => ({
            ...prev,
            [activeStudyUid]: {
                ...prev[activeStudyUid],
                findings: e.target.value
            }
        }));
    };

    // entryKey is the series UID, or "<uid>::<part>" when a split series contributes
    // several independently-configured entries (see SeriesPicker).
    const handleSeriesUpdate = (entryKey, seriesData, action) => {
        // If selecting the first series for this study, move it to the top
        const currentSelection = draftContent[activeStudyUid]?.seriesSelection || {};
        if (action !== 'deselect' && Object.keys(currentSelection).length === 0) {
            moveStudyToSelectedEnd(activeStudyUid);
        }

        setDraftContent(prev => {
            const studyState = prev[activeStudyUid];
            const currentSeriesState = studyState.seriesSelection || {};
            let newSeriesState = { ...currentSeriesState };

            if (action === 'deselect') {
                delete newSeriesState[entryKey];
            } else {
                newSeriesState[entryKey] = {
                    ...seriesData,
                    redactions: seriesData.redactions || []
                };
            }

            return {
                ...prev,
                [activeStudyUid]: { ...studyState, seriesSelection: newSeriesState }
            };
        });
    };

    const handleDiscard = () => {
        if (window.confirm("Are you sure you want to discard this draft?")) {
            resetToSearch();
        }
    };

    const buildSubmissionPayload = () => {
        // Iterate patientStudies to preserve the user-defined order
        const studiesPayload = patientStudies
            .filter(study => Object.keys(draftContent[study.studyInstanceUid]?.seriesSelection || {}).length > 0)
            .map(study => {
                const uid = study.studyInstanceUid;
                const data = draftContent[uid];
                const seriesPayload = Object.entries(data.seriesSelection).map(([entryKey, sData]) => {
                    // A split series contributes several entries whose keys are "<uid>::<part>",
                    // so the source series UID comes from the entry itself, not the key.
                    const sUid = sData.seriesInstanceUid || entryKey;
                    const originalSeries = loadedSeriesData[uid]?.find(s => s.seriesInstanceUid === sUid);
                    return {
                        seriesinstanceuid: sUid,
                        seriesdescription: originalSeries?.seriesDescription || '',
                        modality: originalSeries?.modality || '',
                        subseriesKey: sData.subseriesKey ?? null,
                        subseriesLabel: sData.subseriesLabel ?? null,
                        sopInstanceUids: sData.sopInstanceUids ?? [],
                        start: sData.start,
                        end: sData.end,
                        step: sData.step,
                        redactions: sData.redactions || [],
                        // sData.uploadMethod === 'png' means this series was force-flagged
                        // (redactions applied, or multiframe culling). Otherwise fall back
                        // to the global case-level preference.
                        uploadMethod: sData.uploadMethod === 'png' ? 'png' : uploadMethod
                    };
                });
                return {
                    studyinstanceuid: uid,
                    modality: study.modality,
                    findings: data.findings,
                    remoteNodeName: study.remoteNodeName,
                    series: seriesPayload
                };
            });

        if (studiesPayload.length === 0) {
            return null;
        }

        // Append mode only sends the new studies/series — case details already exist
        if (appendTarget) {
            return { studies: studiesPayload };
        }

        const systemId = SYSTEM_MAP[caseData.system];
        const certaintyId = Number(caseData.diagnostic_certainty);

        if (!systemId) {
            return null;
        }

        return {
            title: caseData.title,
            presentation: caseData.presentation,
            age: caseData.age,
            sex: caseData.sex,
            body: caseDiscussion,
            system: systemId,
            diagnostic_certainty: certaintyId || 1,
            patientName: patientInfo?.name || '',
            patientId: patientInfo?.id || '',
            patientDob: patientInfo?.dob || null,
            studies: studiesPayload
        };
    };

    const submitCaseToServer = async (payload) => {
        const url = appendTarget
            ? `/api/cases/${appendTarget.id}/append`
            : '/api/cases/submit';
        try {
            const res = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (res.ok) {
                const data = await res.json();
                setSubmittedCaseId(data.caseId);
                setShowSuccessModal(true);
            } else {
                const err = await res.text();
                alert("Submission failed: " + err);
            }
        } catch (e) {
            console.error(e);
            alert("Network error submitting case.");
        }
    };

    const checkPatientDuplicates = async (patientId) => {
        if (!patientId) return [];

        try {
            const res = await fetch(`/api/cases/check-patient/${encodeURIComponent(patientId)}`);
            if (res.ok) {
                const data = await res.json();
                return data.cases || [];
            }
        } catch (e) {
            console.error("Failed to check patient duplicates:", e);
        }
        return [];
    };

    const handleSubmit = async () => {
        if (!validateForm()) {
            alert("Please correct the errors in the Case Details section.");
            return;
        }

        const payload = buildSubmissionPayload();

        if (!payload) {
            alert("Please select at least one series and ensure all required fields are filled.");
            return;
        }

        await submitCaseToServer(payload);
    };

    const handleDuplicateWarningClose = () => {
        setShowDuplicateWarning(false);
        setPendingDraftStudy(null);
        setExistingPatientCases([]);
    };

    const handleDuplicateWarningContinue = async () => {
        setShowDuplicateWarning(false);
        setExistingPatientCases([]);
        if (pendingDraftStudy) {
            await proceedWithDraftInit(pendingDraftStudy);
        }
        setPendingDraftStudy(null);
    };

    const handleAddToExistingCase = async (caseItem) => {
        setShowDuplicateWarning(false);
        const fallbackNode = pendingDraftStudy?.remoteNodeName;
        setPendingDraftStudy(null);
        setExistingPatientCases([]);
        await enterAppendMode(caseItem.id, fallbackNode);
    };

    const handleViewExistingCase = (caseId) => {
        // Hide the warning modal and show the case detail drawer
        setShowDuplicateWarning(false);
        setSelectedCaseId(caseId);
        setDrawerOpen(true);
    };

    const handleDrawerClose = () => {
        setDrawerOpen(false);
        setSelectedCaseId(null);
        // Re-show the warning modal if there is still a pending draft study
        if (pendingDraftStudy && existingPatientCases.length > 0) {
            setShowDuplicateWarning(true);
        }
        // Re-show the success modal if there is a submitted case
        if (submittedCaseId) {
            setShowSuccessModal(true);
        }
    };

    const handleSuccessGoToMyCases = () => {
        setShowSuccessModal(false);
        setSubmittedCaseId(null);
        resetToSearch();
        window.location.href = '/my-cases';
    };

    const handleSuccessAddNewCase = () => {
        setShowSuccessModal(false);
        setSubmittedCaseId(null);
        resetToSearch();
    };

    const handleSuccessViewCase = (caseId) => {
        setShowSuccessModal(false);
        setSelectedCaseId(caseId);
        setDrawerOpen(true);
    };

    const activeStudy = patientStudies.find(s => s.studyInstanceUid === activeStudyUid);
    const activeDraftData = draftContent[activeStudyUid] || { findings: '', seriesSelection: {} };

    // Studies already uploaded as part of the append target case
    const studyUidsInCase = new Set((appendTarget?.studies || []).map(s => s.studyInstanceUid));
    const activeStudyInCase = !!appendTarget && studyUidsInCase.has(activeStudyUid);

    const totalStudiesCount = patientStudies.length;
    const selectedStudiesCount = patientStudies.reduce((acc, s) => {
        const seriesCount = Object.keys(draftContent[s.studyInstanceUid]?.seriesSelection || {}).length;
        return acc + (seriesCount > 0 ? 1 : 0);
    }, 0);

    return (
        <MainLayout user={user} onLogout={onLogout}>
            <div className="p-6 max-w-[1800px] mx-auto space-y-6">

                {viewMode === 'search' ? (
                    <div>
                        <h1 className="text-2xl font-bold text-slate-900 dark:text-white mb-6">PACS Search</h1>
                        <div className="grid grid-cols-1 xl:grid-cols-2 gap-8 items-start">
                            <div className="xl:sticky xl:top-6">
                                <PacsSearchForm onSearch={handleSearch} loading={isSearching} />
                            </div>
                            <div>
                                <StudyList
                                    studies={studies}
                                    loading={isSearching}
                                    hasSearched={hasSearched}
                                    selectedStudy={selectedSearchStudy}
                                    onSelect={setSelectedSearchStudy}
                                    onDraft={handleInitializeDraft}
                                    isDrafting={isInitializingDraft}
                                />
                            </div>
                        </div>
                    </div>
                ) : (
                    <div className="animate-fade-in-up space-y-6">
                        {/* 1. TOP SECTION */}
                        <div className="grid grid-cols-1 xl:grid-cols-2 gap-6 xl:h-[650px]">
                            {/* LEFT COLUMN: Patient Info & Study List */}
                            <div className="flex flex-col gap-4 h-full min-h-0">
                                {/* Patient Info Card */}
                                <div className="flex-none bg-white dark:bg-slate-800 shadow rounded-lg p-4 border-l-4 border-indigo-500 flex justify-between items-center">
                                    <div>
                                        <div className="flex items-baseline gap-2">
                                            <h2 className="text-lg font-bold text-slate-900 dark:text-white">{patientInfo?.name}</h2>
                                            <span className="text-sm font-mono text-slate-500">{patientInfo?.id}</span>
                                        </div>
                                        <div className="text-sm text-slate-600 dark:text-slate-300">
                                            {patientInfo?.sex} &middot; {new Date(patientInfo?.dob).toLocaleDateString('en-AU')}
                                        </div>
                                    </div>
                                    <button
                                        onClick={handleDiscard}
                                        className="text-xs text-red-600 hover:bg-red-50 border border-red-200 px-3 py-1 rounded transition-colors"
                                    >
                                        Find New Patient
                                    </button>
                                </div>

                                {/* Study List Wrapper */}
                                <div className="flex-1 min-h-0 bg-white dark:bg-slate-800 shadow rounded-lg border border-slate-200 dark:border-slate-700 overflow-hidden flex flex-col">
                                    <div className="flex-none px-4 py-3 bg-slate-50 dark:bg-slate-900/50 border-b border-slate-200 dark:border-slate-700 flex justify-between items-center">
                                        <h3 className="text-sm font-bold text-slate-700 dark:text-slate-300 uppercase tracking-wide">Available Studies</h3>
                                        <span className={`text-xs px-2 py-0.5 rounded-full font-bold ${selectedStudiesCount > 0 ? 'bg-indigo-100 text-indigo-700 dark:bg-indigo-900 dark:text-indigo-300' : 'bg-slate-200 text-slate-600 dark:bg-slate-700 dark:text-slate-300'}`}>
                                            {selectedStudiesCount} / {totalStudiesCount} Selected
                                        </span>
                                    </div>
                                    {/* Scrollable Container */}
                                    <div className="flex-1 overflow-y-auto min-h-0">
                                        <table className="min-w-full divide-y divide-slate-200 dark:divide-slate-700">
                                            <thead className="bg-slate-50 dark:bg-slate-900/20 sticky top-0 z-10 shadow-sm">
                                                <tr>
                                                    <th className="px-4 py-2 text-center text-xs font-medium text-slate-500 uppercase tracking-wider w-12">Status</th>
                                                    <th className="px-4 py-2 text-left text-xs font-medium text-slate-500 uppercase tracking-wider">Date</th>
                                                    <th className="px-4 py-2 text-left text-xs font-medium text-slate-500 uppercase tracking-wider">Description</th>
                                                    <th className="px-4 py-2 text-left text-xs font-medium text-slate-500 uppercase tracking-wider">Mod</th>
                                                </tr>
                                            </thead>
                                            <tbody className="bg-white dark:bg-slate-800 divide-y divide-slate-200 dark:divide-slate-700">
                                                {patientStudies.map((study, index) => {
                                                    const isActive = study.studyInstanceUid === activeStudyUid;
                                                    const seriesCount = Object.keys(draftContent[study.studyInstanceUid]?.seriesSelection || {}).length;
                                                    const hasSelectedSeries = seriesCount > 0;
                                                    return (
                                                        <tr
                                                            key={study.studyInstanceUid}
                                                            draggable={hasSelectedSeries}
                                                            onDragStart={(e) => handleDragStart(e, index)}
                                                            onDragEnter={(e) => handleDragEnter(e, index)}
                                                            onDragEnd={handleDragEnd}
                                                            onDragOver={(e) => e.preventDefault()}
                                                            onClick={() => handleSwitchActiveStudy(study)}
                                                            className={`cursor-pointer transition-colors border-l-4 ${isActive
                                                                ? 'bg-indigo-50 dark:bg-indigo-900/30 border-l-indigo-500'
                                                                : 'hover:bg-slate-50 dark:hover:bg-slate-700 border-l-transparent'
                                                                } ${hasSelectedSeries ? 'cursor-move' : ''}`}
                                                        >
                                                            <td className="px-4 py-3 whitespace-nowrap text-center">
                                                                {hasSelectedSeries ? (
                                                                    <div className="flex items-center justify-center gap-2">
                                                                        <svg className="w-4 h-4 text-slate-400 hover:text-slate-600" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 8h16M4 16h16" /></svg>
                                                                        <div className="w-5 h-5 bg-green-100 dark:bg-green-900 rounded-full text-green-600 dark:text-green-400 flex items-center justify-center">
                                                                            <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7" /></svg>
                                                                        </div>
                                                                    </div>
                                                                ) : (
                                                                    isActive && (
                                                                        <div className="mx-auto flex items-center justify-center w-6 h-6 rounded-full text-indigo-400">
                                                                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" /><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" /></svg>
                                                                        </div>
                                                                    )
                                                                )}
                                                            </td>
                                                            <td className="px-4 py-3 whitespace-nowrap text-sm text-slate-500 dark:text-slate-400">
                                                                <div className="flex flex-col">
                                                                    <span>{new Date(study.studyDate).toLocaleDateString('en-AU')}</span>
                                                                    {isActive && !hasSelectedSeries && <span className="text-[10px] text-indigo-600 font-bold uppercase">Viewing</span>}
                                                                </div>
                                                            </td>
                                                            <td className="px-4 py-3 text-sm text-slate-900 dark:text-slate-200">
                                                                <div className={`font-medium flex items-center gap-2 ${hasSelectedSeries ? 'text-green-700 dark:text-green-400' : ''}`}>
                                                                    {study.studyDescription || 'No description'}
                                                                    {studyUidsInCase.has(study.studyInstanceUid) && (
                                                                        <span className="px-1.5 py-0.5 text-[10px] font-bold uppercase rounded bg-emerald-100 text-emerald-700 dark:bg-emerald-900 dark:text-emerald-300 whitespace-nowrap" title="This study is already part of the case — selected series will be added to it">
                                                                            In case
                                                                        </span>
                                                                    )}
                                                                </div>
                                                                {hasSelectedSeries && (
                                                                    <div className="text-xs text-green-600 mt-1 flex items-center">
                                                                        {seriesCount} series selected
                                                                    </div>
                                                                )}
                                                            </td>
                                                            <td className="px-4 py-3 whitespace-nowrap text-sm">
                                                                <span className="px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full bg-slate-100 text-slate-800 dark:bg-slate-700 dark:text-slate-300">
                                                                    {study.modality}
                                                                </span>
                                                            </td>
                                                        </tr>
                                                    );
                                                })}
                                            </tbody>
                                        </table>
                                    </div>

                                </div>
                            </div>
                            {/* RIGHT COLUMN: Case Details Form, or target case summary when appending */}
                            <div className="h-full min-h-0">
                                {appendTarget ? (
                                    <div className="bg-white dark:bg-slate-800 shadow rounded-lg border border-emerald-300 dark:border-emerald-800 overflow-hidden">
                                        <div className="px-4 py-3 bg-emerald-50 dark:bg-emerald-900/20 border-b border-emerald-200 dark:border-emerald-800 flex items-center gap-2">
                                            <svg className="w-5 h-5 text-emerald-600 dark:text-emerald-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4v16m8-8H4" />
                                            </svg>
                                            <h3 className="text-sm font-bold text-emerald-800 dark:text-emerald-300 uppercase tracking-wide">
                                                Adding to Existing Case
                                            </h3>
                                        </div>
                                        <div className="p-4 space-y-3">
                                            <div>
                                                <div className="text-xs text-slate-500 dark:text-slate-400 uppercase tracking-wide mb-1">Case Title</div>
                                                <div className="text-lg font-bold text-slate-900 dark:text-white">
                                                    {appendTarget.title || 'Untitled Case'}
                                                </div>
                                            </div>
                                            <div className="flex items-center gap-4 text-sm text-slate-600 dark:text-slate-300">
                                                <span>Created {new Date(appendTarget.createdAt).toLocaleDateString('en-AU')}</span>
                                                <span>&middot;</span>
                                                <span>{appendTarget.studies?.length || 0} stud{(appendTarget.studies?.length || 0) === 1 ? 'y' : 'ies'} uploaded</span>
                                            </div>
                                            <a
                                                href={`https://radiopaedia.org/cases/${appendTarget.radiopaediaCaseId}`}
                                                target="_blank"
                                                rel="noopener noreferrer"
                                                className="inline-flex items-center gap-1.5 text-sm text-indigo-600 dark:text-indigo-400 hover:text-indigo-700 font-semibold"
                                            >
                                                View case on Radiopaedia
                                                <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                                                </svg>
                                            </a>
                                            <div className="pt-2 border-t border-slate-200 dark:border-slate-700 text-sm text-slate-500 dark:text-slate-400 leading-relaxed">
                                                Select the studies and series to add below. They will be uploaded to this
                                                existing Radiopaedia case — studies already in the case are marked in the
                                                list. Case details (title, presentation, discussion) are not editable here;
                                                edit them on Radiopaedia directly.
                                            </div>
                                        </div>
                                    </div>
                                ) : (
                                    <CaseDetailsForm
                                        formData={caseData}
                                        onChange={handleCaseChange}
                                        errors={errors}
                                        systemMap={SYSTEM_MAP}
                                        certaintyOptions={DIAGNOSTIC_CERTAINTY_OPTIONS}
                                    />
                                )}
                            </div>
                        </div>

                        {/* 2. BOTTOM SECTIONS */}
                        <div className="bg-white dark:bg-slate-800 shadow rounded-lg border border-slate-200 dark:border-slate-700 p-6">
                            <div className="mb-4 pb-4 border-b border-slate-200 dark:border-slate-700 flex justify-between items-center">
                                <h2 className="text-xl font-bold text-slate-900 dark:text-white flex items-center">
                                    <span className="bg-indigo-100 text-indigo-800 text-xs px-2 py-1 rounded mr-3">ACTIVE STUDY</span>
                                    {activeStudy?.studyDescription}
                                </h2>
                                <span className="text-sm text-slate-500">
                                    {activeStudy ? new Date(activeStudy.studyDate).toLocaleDateString('en-AU') : ''}
                                </span>
                            </div>

                            <div className="space-y-6">
                                <SeriesPicker
                                    key={activeStudyUid}
                                    seriesList={loadedSeriesData[activeStudyUid] || []}
                                    selectedSeriesMap={activeDraftData.seriesSelection}
                                    onSeriesUpdate={handleSeriesUpdate}
                                    loading={isLoadingSeries}
                                />

                                {/* ── Upload Format Card ─────────────────────────────────────────────── */}
                                <div className="rounded-lg border border-slate-200 dark:border-slate-700 overflow-hidden">

                                    {/* Header row */}
                                    <div className="px-4 py-3 bg-slate-50 dark:bg-slate-900/50 border-b border-slate-200 dark:border-slate-700 flex items-center justify-between">
                                        <div>
                                            <p className="text-sm font-bold text-slate-700 dark:text-slate-300">Upload Format</p>
                                            <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Applies to all series in this case</p>
                                        </div>
                                        {/* Toggle */}
                                        <div className="flex items-center bg-white dark:bg-slate-700 rounded-md border border-slate-300 dark:border-slate-600 p-0.5 shadow-sm">
                                            <button
                                                onClick={() => setUploadMethod('dicom')}
                                                className={`px-5 py-1.5 text-xs font-bold uppercase rounded transition-colors
                                                    ${uploadMethod === 'dicom'
                                                        ? 'bg-indigo-600 text-white shadow-inner'
                                                        : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-600'}`}
                                            >
                                                DICOM
                                            </button>
                                            <button
                                                onClick={() => setUploadMethod('png')}
                                                className={`px-5 py-1.5 text-xs font-bold uppercase rounded transition-colors
                                                    ${uploadMethod === 'png'
                                                        ? 'bg-slate-600 text-white shadow-inner'
                                                        : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-600'}`}
                                            >
                                                PNG
                                            </button>
                                        </div>
                                    </div>

                                    {/* Description */}
                                    <div className="px-4 py-3 text-sm space-y-2">
                                        {uploadMethod === 'dicom' ? (
                                            <>
                                                <p className="text-slate-700 dark:text-slate-300 leading-relaxed">
                                                    <strong className="text-indigo-700 dark:text-indigo-400">Native DICOM upload:</strong> files
                                                    are anonymised on this system before being sent to Radiopaedia. All patient
                                                    tags are zeroed, non-medical tags are stripped, and UIDs are replaced using
                                                    Radiopaedia&apos;s SHA-512 hashing algorithm.
                                                </p>
                                                <p className="text-xs text-slate-500 dark:text-slate-400">
                                                    Some series are always uploaded as PNG regardless of this setting:
                                                </p>
                                                <ul className="text-xs text-slate-500 dark:text-slate-400 list-disc list-inside space-y-0.5 ml-1">
                                                    <li>Series with image redactions (pixel manipulation requires PNG conversion)</li>
                                                    <li>Multiframe series (single file / many frames) where only a subset of frames is selected; partial frame extraction from multiframe DICOM is not supported</li>
                                                </ul>
                                                <p className="text-xs text-slate-500 dark:text-slate-400 italic">
                                                    Note: DICOM uploads take a little longer for Radiopaedia to process before images become viewable. Please allow a few minutes after a successful upload.
                                                </p>
                                                <button
                                                    onClick={() => setAnonDrawerOpen(true)}
                                                    className="inline-flex items-center gap-1.5 text-xs text-indigo-600 dark:text-indigo-400 hover:text-indigo-700 dark:hover:text-indigo-300 font-semibold pt-0.5"
                                                >
                                                    View full anonymisation details
                                                    <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 7l5 5m0 0l-5 5m5-5H6" />
                                                    </svg>
                                                </button>
                                            </>
                                        ) : (
                                            <p className="text-slate-700 dark:text-slate-300 leading-relaxed">
                                                <strong className="text-sky-700 dark:text-sky-400">PNG conversion:</strong> DICOM files
                                                are decoded and rendered to PNG images before upload. Only pixel data is sent;
                                                all DICOM header metadata (patient name, dates, scanner details, etc.) is
                                                automatically stripped during conversion.
                                            </p>
                                        )}
                                    </div>

                                    {/* Always visible — burnt-in warning */}
                                    <div className="px-4 py-3 bg-amber-50 dark:bg-amber-950/20 border-t border-amber-100 dark:border-amber-800/40 flex items-start gap-3">
                                        <svg className="w-4 h-4 text-amber-500 dark:text-amber-400 flex-shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                                        </svg>
                                        <p className="text-amber-800 dark:text-amber-300 text-sm leading-relaxed">
                                            <strong>Burnt-in pixel data:</strong> Text overlaid directly onto image pixels
                                            (e.g. patient name stamped on the scan) cannot be removed by any metadata-level
                                            process; this applies to both DICOM and PNG modes. Use the{' '}
                                            <strong>Redact</strong> tool in the series viewer to draw over any identifying
                                            regions before submitting.
                                        </p>
                                    </div>
                                </div>

                                <div>
                                    <label className="block text-sm font-bold text-slate-700 dark:text-slate-300 mb-2">Study Findings</label>
                                    {activeStudyInCase ? (
                                        <div className="rounded-md border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900/50 px-4 py-3 text-sm text-slate-500 dark:text-slate-400">
                                            This study already exists on the case, so selected series will be added to it
                                            directly. Its findings cannot be changed here — edit them on Radiopaedia.
                                        </div>
                                    ) : (
                                        <textarea
                                            value={activeDraftData.findings}
                                            onChange={handleStudyFindingsChange}
                                            rows={8}
                                            className="w-full rounded-md border-slate-300 dark:bg-slate-900 dark:border-slate-600 shadow-sm sm:text-sm py-3 px-4 border resize-y min-h-[150px]"
                                            placeholder="Describe findings specific to this study..."
                                        />
                                    )}
                                </div>
                            </div>
                        </div>

                        {/* Case Discussion (fixed when appending — edit on Radiopaedia instead) */}
                        {!appendTarget && (
                            <div className="bg-white dark:bg-slate-800 shadow rounded-lg border border-slate-200 dark:border-slate-700 p-6">
                                <label className="block text-lg font-bold text-slate-900 dark:text-white mb-3">Case Discussion</label>
                                <textarea
                                    value={caseDiscussion}
                                    onChange={(e) => setCaseDiscussion(e.target.value)}
                                    rows={8}
                                    className="w-full rounded-md border-slate-300 dark:bg-slate-900 dark:border-slate-600 shadow-sm sm:text-sm py-3 px-4 border"
                                    placeholder="Comprehensive discussion of the case, diagnosis, and differential..."
                                />
                            </div>
                        )}

                        <div className="flex justify-end gap-4 pt-4 pb-12">
                            <button
                                onClick={handleDiscard}
                                className="px-6 py-4 bg-slate-200 text-slate-700 text-lg font-medium rounded shadow hover:bg-slate-300 transition-all"
                            >
                                Discard
                            </button>
                            <button
                                onClick={handleSubmit}
                                className="px-8 py-4 bg-indigo-600 text-white text-lg font-bold rounded shadow-xl hover:bg-indigo-700 focus:ring-4 focus:ring-indigo-500/50 transition-all transform hover:-translate-y-0.5"
                            >
                                {appendTarget ? 'Upload Additional Studies to Case' : 'Upload Anonymised Draft Case'}
                            </button>
                        </div>
                    </div>
                )}
            </div>

            {/* Duplicate Patient Warning Modal */}
            <DuplicatePatientWarningModal
                isOpen={showDuplicateWarning}
                onClose={handleDuplicateWarningClose}
                onContinue={handleDuplicateWarningContinue}
                onViewCase={handleViewExistingCase}
                onAddToCase={handleAddToExistingCase}
                existingCases={existingPatientCases}
                patientName={pendingDraftStudy?.patientName?.replace('^', ', ')}
                patientId={pendingDraftStudy?.patientId}
            />

            {/* Submission Success Modal */}
            <SubmissionSuccessModal
                isOpen={showSuccessModal}
                caseId={submittedCaseId}
                isAppend={!!appendTarget}
                onGoToMyCases={handleSuccessGoToMyCases}
                onAddNewCase={handleSuccessAddNewCase}
                onViewCase={handleSuccessViewCase}
            />

            {/* Case Detail Drawer */}
            <CaseDetailDrawer
                isOpen={drawerOpen}
                onClose={handleDrawerClose}
                caseId={selectedCaseId}
                zIndex={60}
            />

            {/* DICOM Anonymisation Details Drawer */}
            <AnonymisationDrawer
                isOpen={anonDrawerOpen}
                onClose={() => setAnonDrawerOpen(false)}
            />
        </MainLayout>
    );
};

export default DashboardPage;