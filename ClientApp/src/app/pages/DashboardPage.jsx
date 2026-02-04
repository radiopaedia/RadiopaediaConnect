import { useState, useRef } from 'react';
import MainLayout from './MainLayout';
import PacsSearchForm from './components/PacsSearchForm';
import CaseDetailsForm from './components/CaseDetailsForm';
import SeriesPicker from './components/SeriesPicker';
import StudyList from './components/StudyList';
import CaseDetailDrawer from './CaseDetailDrawer';
import DuplicatePatientWarningModal from './DuplicatePatientWarningModal';

// --- Radiopaedia Constants ---
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

    // --- Search State ---
    const [studies, setStudies] = useState([]);
    const [isSearching, setIsSearching] = useState(false);
    const [hasSearched, setHasSearched] = useState(false);
    const [selectedSearchStudy, setSelectedSearchStudy] = useState(null);
    const [isInitializingDraft, setIsInitializingDraft] = useState(false);

    // --- Draft Master State ---
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

    // --- Duplicate Patient Warning State ---
    const [showDuplicateWarning, setShowDuplicateWarning] = useState(false);
    const [existingPatientCases, setExistingPatientCases] = useState([]);
    const [pendingPayload, setPendingPayload] = useState(null);

    // --- Case Detail Drawer State ---
    const [drawerOpen, setDrawerOpen] = useState(false);
    const [selectedCaseId, setSelectedCaseId] = useState(null);

    // --- Drag & Drop State ---
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

    // --- HELPER: Full Reset ---
    // Clears everything to return to a pristine "Search" state
    const resetToSearch = () => {
        // 1. Reset Search Context
        setStudies([]);
        setHasSearched(false);
        setSelectedSearchStudy(null);
        setIsSearching(false);
        setIsInitializingDraft(false);

        // 2. Reset Draft Context
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

        // 3. Navigate
        setViewMode('search');
    };

    // --- Search Logic ---
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

    // --- Initialize Draft ---
    const handleInitializeDraft = async () => {
        if (!selectedSearchStudy) return;

        setIsInitializingDraft(true);
        try {
            const primaryStudy = selectedSearchStudy;

            // 1. Fetch comprehensive patient history using Patient ID
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

            // Sort by date descending
            relatedStudies.sort((a, b) => new Date(b.studyDate) - new Date(a.studyDate));

            // 2. Setup Patient Info
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

    // --- Validation ---
    const validateForm = () => {
        const newErrors = {};
        if (!caseData.title.trim()) newErrors.title = 'Title is required';
        if (!caseData.system) newErrors.system = 'System is required';
        if (!caseData.presentation.trim()) newErrors.presentation = 'Presentation is required';
        if (!caseData.age.toString().trim()) newErrors.age = 'Age is required';
        if (!caseData.sex) newErrors.sex = 'Sex is required';

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    };

    // --- Handlers ---
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

    const handleSeriesUpdate = (seriesUid, seriesData, action) => {
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
                delete newSeriesState[seriesUid];
            } else {
                newSeriesState[seriesUid] = {
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

    // --- Build Payload Helper ---
    const buildSubmissionPayload = () => {
        // Iterate patientStudies to preserve the user-defined order
        const studiesPayload = patientStudies
            .filter(study => Object.keys(draftContent[study.studyInstanceUid]?.seriesSelection || {}).length > 0)
            .map(study => {
                const uid = study.studyInstanceUid;
                const data = draftContent[uid];
                const seriesPayload = Object.entries(data.seriesSelection).map(([sUid, sData]) => {
                    const originalSeries = loadedSeriesData[uid]?.find(s => s.seriesInstanceUid === sUid);
                    return {
                        seriesinstanceuid: sUid,
                        seriesdescription: originalSeries?.seriesDescription || '',
                        modality: originalSeries?.modality || '',
                        start: sData.start,
                        end: sData.end,
                        step: sData.step,
                        redactions: sData.redactions || []
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

    // --- Submit the payload to the server ---
    const submitCaseToServer = async (payload) => {
        try {
            const res = await fetch('/api/cases/submit', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (res.ok) {
                const data = await res.json();
                alert(`Case Submitted Successfully! ID: ${data.caseId}`);
                resetToSearch();
            } else {
                const err = await res.text();
                alert("Submission failed: " + err);
            }
        } catch (e) {
            console.error(e);
            alert("Network error submitting case.");
        }
    };

    // --- Check for existing patient cases ---
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

    // --- Submit Logic ---
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

        // Check for existing cases with same patient ID
        const existingCases = await checkPatientDuplicates(patientInfo?.id);

        if (existingCases.length > 0) {
            // Store the payload and show warning modal
            setPendingPayload(payload);
            setExistingPatientCases(existingCases);
            setShowDuplicateWarning(true);
        } else {
            // No duplicates, proceed with submission
            await submitCaseToServer(payload);
        }
    };

    // --- Handle warning modal actions ---
    const handleDuplicateWarningClose = () => {
        setShowDuplicateWarning(false);
        setPendingPayload(null);
        setExistingPatientCases([]);
    };

    const handleDuplicateWarningContinue = async () => {
        setShowDuplicateWarning(false);
        if (pendingPayload) {
            await submitCaseToServer(pendingPayload);
        }
        setPendingPayload(null);
        setExistingPatientCases([]);
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
        // Re-show the warning modal if there's still a pending payload
        if (pendingPayload && existingPatientCases.length > 0) {
            setShowDuplicateWarning(true);
        }
    };

    const activeStudy = patientStudies.find(s => s.studyInstanceUid === activeStudyUid);
    const activeDraftData = draftContent[activeStudyUid] || { findings: '', seriesSelection: {} };

    // --- Counts ---
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
                                            {patientInfo?.sex} &middot; {new Date(patientInfo?.dob).toLocaleDateString()}
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
                                                                    <span>{new Date(study.studyDate).toLocaleDateString()}</span>
                                                                    {isActive && !hasSelectedSeries && <span className="text-[10px] text-indigo-600 font-bold uppercase">Viewing</span>}
                                                                </div>
                                                            </td>
                                                            <td className="px-4 py-3 text-sm text-slate-900 dark:text-slate-200">
                                                                <div className={`font-medium ${hasSelectedSeries ? 'text-green-700 dark:text-green-400' : ''}`}>
                                                                    {study.studyDescription || 'No description'}
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
                                    <div className="flex-none p-3 bg-yellow-50 dark:bg-yellow-900/10 border-t border-yellow-100 dark:border-yellow-800 text-[11px] text-yellow-800 dark:text-yellow-200">
                                        <strong>Note:</strong> Patient metadata is anonymised on upload. Information burnt into images (pixel data) must be redacted manually.
                                    </div>
                                </div>
                            </div>
                            {/* RIGHT COLUMN: Case Details Form */}
                            <div className="h-full min-h-0">
                                <CaseDetailsForm
                                    formData={caseData}
                                    onChange={handleCaseChange}
                                    errors={errors}
                                    systemMap={SYSTEM_MAP}
                                    certaintyOptions={DIAGNOSTIC_CERTAINTY_OPTIONS}
                                />
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
                                    {activeStudy ? new Date(activeStudy.studyDate).toLocaleDateString() : ''}
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
                                <div>
                                    <label className="block text-sm font-bold text-slate-700 dark:text-slate-300 mb-2">Study Findings</label>
                                    <textarea
                                        value={activeDraftData.findings}
                                        onChange={handleStudyFindingsChange}
                                        rows={8}
                                        className="w-full rounded-md border-slate-300 dark:bg-slate-900 dark:border-slate-600 shadow-sm sm:text-sm py-3 px-4 border resize-y min-h-[150px]"
                                        placeholder="Describe findings specific to this study..."
                                    />
                                </div>
                            </div>
                        </div>

                        {/* Case Discussion */}
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
                                Upload Anonymised Draft Case
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
                existingCases={existingPatientCases}
                patientName={patientInfo?.name}
                patientId={patientInfo?.id}
            />

            {/* Case Detail Drawer */}
            <CaseDetailDrawer
                isOpen={drawerOpen}
                onClose={handleDrawerClose}
                caseId={selectedCaseId}
                zIndex={60}
            />
        </MainLayout>
    );
};

export default DashboardPage;