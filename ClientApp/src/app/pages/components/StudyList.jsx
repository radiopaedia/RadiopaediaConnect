const StudyList = ({
    studies,
    loading,
    hasSearched,
    selectedStudy,
    onSelect,
    onDraft,
    isDrafting
}) => {

    const formatDate = (dateString, includeTime = true) => {
        if (!dateString) return '';
        const opts = includeTime
            ? { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }
            : { year: 'numeric', month: 'short', day: 'numeric' };

        return new Date(dateString).toLocaleDateString('en-US', opts);
    };

    const getModalityColor = (modality) => {
        switch (modality) {
            case 'CT': return 'bg-blue-100 text-blue-800 border-blue-200';
            case 'MR': return 'bg-purple-100 text-purple-800 border-purple-200';
            case 'XR':
            case 'CR': return 'bg-gray-100 text-gray-800 border-gray-200';
            case 'US': return 'bg-green-100 text-green-800 border-green-200';
            default: return 'bg-slate-100 text-slate-800 border-slate-200';
        }
    };

    if (loading) {
        return (
            <div className="bg-white dark:bg-slate-800 shadow rounded-lg p-12 flex flex-col items-center justify-center min-h-[400px]">
                <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
                <p className="mt-4 text-slate-500">Querying PACS...</p>
            </div>
        );
    }

    if (!hasSearched && studies.length === 0) {
        return (
            <div className="bg-slate-50 dark:bg-slate-800/50 border-2 border-dashed border-slate-300 dark:border-slate-700 rounded-lg p-12 flex flex-col items-center justify-center min-h-[400px] text-center">
                <svg className="w-16 h-16 text-slate-300 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
                </svg>
                <h3 className="text-lg font-medium text-slate-900 dark:text-white">No studies loaded</h3>
                <p className="text-slate-500">Use the search panel to find studies.</p>
            </div>
        );
    }

    if (hasSearched && studies.length === 0) {
        return (
            <div className="bg-white dark:bg-slate-800 shadow rounded-lg p-8 text-center min-h-[200px] flex flex-col items-center justify-center">
                <p className="text-slate-500">No studies found matching those criteria.</p>
            </div>
        );
    }

    return (
        <div className="bg-white dark:bg-slate-800 shadow rounded-lg overflow-hidden border border-slate-200 dark:border-slate-700 flex flex-col">
            {/* Header */}
            <div className="px-6 py-4 border-b border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900/50 flex justify-between items-center">
                <h3 className="text-lg font-medium text-slate-900 dark:text-white">
                    Study Results
                    <span className="ml-2 text-sm font-normal text-slate-500 bg-slate-200 dark:bg-slate-700 px-2 py-0.5 rounded-full">
                        {studies.length}
                    </span>
                </h3>
            </div>

            {/* Scrollable List */}
            <ul className="divide-y divide-slate-200 dark:divide-slate-700 max-h-[600px] overflow-y-auto">
                {studies.map((study) => {
                    const isSelected = selectedStudy?.studyInstanceUid === study.studyInstanceUid;

                    return (
                        <li
                            key={study.studyInstanceUid}
                            onClick={() => onSelect(study)}
                            className={`
                                cursor-pointer hover:bg-indigo-50 dark:hover:bg-indigo-900/20 transition-colors duration-150
                                ${isSelected ? 'bg-indigo-50 dark:bg-indigo-900/30 border-l-4 border-indigo-500' : 'border-l-4 border-transparent'}
                            `}
                        >
                            <div className="px-6 py-4">

                                {/* Row 1: Patient Information */}
                                <div className="flex flex-col sm:flex-row sm:items-baseline mb-2 pb-2 border-b border-slate-100 dark:border-slate-700/50">
                                    <span className="text-slate-900 dark:text-slate-100 font-bold text-base mr-3">
                                        {study.patientName?.replace('^', ', ') || "Unknown Name"}
                                    </span>
                                    <div className="flex items-center space-x-3 text-xs text-slate-500 dark:text-slate-400">
                                        <span className="font-mono bg-slate-100 dark:bg-slate-700 px-1.5 py-0.5 rounded">
                                            ID: {study.patientId}
                                        </span>
                                        {study.patientBirthDate && (
                                            <span>
                                                DOB: {formatDate(study.patientBirthDate, false)}
                                            </span>
                                        )}
                                        {study.patientSex && (
                                            <span>
                                                ({study.patientSex})
                                            </span>
                                        )}
                                    </div>
                                </div>

                                {/* Row 2: Study Description */}
                                <h4 className="text-sm font-medium text-indigo-700 dark:text-indigo-400 mb-2">
                                    {study.studyDescription || "(No Study Description)"}
                                </h4>

                                {/* Row 3: Meta Data (Modality, Date, ACC, Count) */}
                                <div className="flex flex-wrap items-center justify-between text-xs text-slate-600 dark:text-slate-400 mt-2">
                                    <div className="flex items-center space-x-3">
                                        <span className={`px-2 py-0.5 rounded font-bold border ${getModalityColor(study.modality)}`}>
                                            {study.modality}
                                        </span>
                                        <span>
                                            {formatDate(study.studyDate)}
                                        </span>
                                        <span>
                                            ACC: <span className="font-mono text-slate-700 dark:text-slate-300">{study.accessionNumber}</span>
                                        </span>
                                    </div>

                                    <div className="flex items-center mt-2 sm:mt-0">
                                        <svg className="w-4 h-4 mr-1 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
                                        </svg>
                                        {study.instanceCount} images

                                        {isSelected && (
                                            <span className="ml-3 flex items-center text-indigo-600 font-bold uppercase tracking-wide">
                                                <svg className="w-4 h-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7" />
                                                </svg>
                                                Selected
                                            </span>
                                        )}
                                    </div>
                                </div>
                            </div>
                        </li>
                    );
                })}
            </ul>

            {/* Footer Action Bar */}
            <div className="p-4 border-t border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900/50 flex justify-end">
                <button
                    onClick={onDraft}
                    disabled={!selectedStudy || isDrafting}
                    className={`
                        flex items-center justify-center px-6 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white transition-all
                        ${!selectedStudy || isDrafting
                            ? 'bg-slate-400 cursor-not-allowed opacity-75'
                            : 'bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500'
                        }
                    `}
                >
                    {isDrafting ? (
                        <>
                            <svg className="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                            </svg>
                            Initializing Case...
                        </>
                    ) : (
                        'Select & Continue'
                    )}
                </button>
            </div>
        </div>
    );
};

export default StudyList;