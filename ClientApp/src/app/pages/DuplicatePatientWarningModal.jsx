import { Fragment } from 'react';
import { Transition, Dialog } from '@headlessui/react';

/**
 * Modal component that warns users when selecting a study for a patient
 * who already has existing cases in the system. Displayed before
 * transitioning to the draft editor.
 * 
 * @param {boolean} isOpen - Controls modal visibility
 * @param {function} onClose - Callback when modal is closed/cancelled
 * @param {function} onContinue - Callback when user chooses to continue with draft creation
 * @param {function} onViewCase - Callback when user clicks to view a case (receives caseId)
 * @param {Array} existingCases - Array of existing case objects for the patient
 * @param {string} patientName - Display name of the patient
 * @param {string} patientId - Patient MRN/ID
 */
const DuplicatePatientWarningModal = ({
    isOpen,
    onClose,
    onContinue,
    onViewCase,
    existingCases = [],
    patientName,
    patientId
}) => {
    const formatDate = (dateString) => {
        if (!dateString) return '';
        return new Date(dateString).toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric'
        });
    };

    const getStatusBadge = (status) => {
        const configs = {
            'Queued': 'bg-yellow-100 text-yellow-800',
            'Processing': 'bg-blue-100 text-blue-800',
            'Completed': 'bg-green-100 text-green-800',
            'Failed': 'bg-red-100 text-red-800'
        };
        return configs[status] || configs['Queued'];
    };

    return (
        <Transition show={isOpen} as={Fragment}>
            <Dialog as="div" className="relative z-40" onClose={onClose}>
                {/* Backdrop */}
                <Transition.Child
                    as={Fragment}
                    enter="ease-out duration-300"
                    enterFrom="opacity-0"
                    enterTo="opacity-100"
                    leave="ease-in duration-200"
                    leaveFrom="opacity-100"
                    leaveTo="opacity-0"
                >
                    <div className="fixed inset-0 bg-black/50 transition-opacity" />
                </Transition.Child>

                {/* Modal */}
                <div className="fixed inset-0 z-10 overflow-y-auto">
                    <div className="flex min-h-full items-center justify-center p-4">
                        <Transition.Child
                            as={Fragment}
                            enter="ease-out duration-300"
                            enterFrom="opacity-0 scale-95"
                            enterTo="opacity-100 scale-100"
                            leave="ease-in duration-200"
                            leaveFrom="opacity-100 scale-100"
                            leaveTo="opacity-0 scale-95"
                        >
                            <Dialog.Panel className="w-full max-w-lg transform overflow-hidden rounded-xl bg-white dark:bg-slate-800 shadow-2xl transition-all">
                                {/* Header */}
                                <div className="bg-amber-50 dark:bg-amber-900/20 px-6 py-4 border-b border-amber-200 dark:border-amber-800">
                                    <div className="flex items-start gap-3">
                                        <div className="flex-shrink-0 w-10 h-10 rounded-full bg-amber-100 dark:bg-amber-900 flex items-center justify-center">
                                            <svg className="w-5 h-5 text-amber-600 dark:text-amber-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                                            </svg>
                                        </div>
                                        <div>
                                            <Dialog.Title className="text-lg font-bold text-amber-800 dark:text-amber-200">
                                                Existing Cases Found
                                            </Dialog.Title>
                                            <p className="text-sm text-amber-700 dark:text-amber-300 mt-1">
                                                This patient already has {existingCases.length} case{existingCases.length !== 1 ? 's' : ''} uploaded.
                                            </p>
                                        </div>
                                    </div>
                                </div>

                                {/* Patient Info */}
                                <div className="px-6 py-3 bg-slate-50 dark:bg-slate-900/30 border-b border-slate-200 dark:border-slate-700">
                                    <div className="flex items-center gap-3">
                                        <div className="w-8 h-8 rounded-full bg-indigo-100 dark:bg-indigo-900 flex items-center justify-center">
                                            <svg className="w-4 h-4 text-indigo-600 dark:text-indigo-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                                            </svg>
                                        </div>
                                        <div>
                                            <div className="font-medium text-slate-900 dark:text-white">
                                                {patientName || 'Unknown Patient'}
                                            </div>
                                            {patientId && (
                                                <div className="text-xs text-slate-500 dark:text-slate-400 font-mono">
                                                    ID: {patientId}
                                                </div>
                                            )}
                                        </div>
                                    </div>
                                </div>

                                {/* Existing Cases List */}
                                <div className="px-6 py-4 max-h-64 overflow-y-auto">
                                    <p className="text-sm text-slate-600 dark:text-slate-400 mb-3">
                                        Click on a case to view its details:
                                    </p>
                                    <div className="space-y-2">
                                        {existingCases.map((caseItem) => (
                                            <button
                                                key={caseItem.id}
                                                onClick={() => onViewCase(caseItem.id)}
                                                className="w-full text-left p-3 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-700/50 transition-colors group"
                                            >
                                                <div className="flex items-start justify-between">
                                                    <div className="flex-1 min-w-0">
                                                        <div className="font-medium text-slate-900 dark:text-white truncate group-hover:text-indigo-600 dark:group-hover:text-indigo-400">
                                                            {caseItem.title || 'Untitled Case'}
                                                        </div>
                                                        <div className="text-xs text-slate-500 dark:text-slate-400 mt-1 flex items-center gap-2">
                                                            <span>{formatDate(caseItem.createdAt)}</span>
                                                            {caseItem.age && (
                                                                <>
                                                                    <span>{'\u00B7'}</span>
                                                                    <span>{caseItem.age}</span>
                                                                </>
                                                            )}
                                                        </div>
                                                    </div>
                                                    <div className="flex items-center gap-2 ml-3">
                                                        <span className={`px-2 py-0.5 text-xs font-medium rounded-full ${getStatusBadge(caseItem.status)}`}>
                                                            {caseItem.status}
                                                        </span>
                                                        <svg className="w-4 h-4 text-slate-400 group-hover:text-indigo-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5l7 7-7 7" />
                                                        </svg>
                                                    </div>
                                                </div>
                                            </button>
                                        ))}
                                    </div>
                                </div>

                                {/* Footer */}
                                <div className="px-6 py-4 bg-slate-50 dark:bg-slate-900/30 border-t border-slate-200 dark:border-slate-700 flex items-center justify-between gap-3">
                                    <p className="text-xs text-slate-500 dark:text-slate-400 flex-1">
                                        Are you sure you want to start a new case for this patient?
                                    </p>
                                    <div className="flex items-center gap-2">
                                        <button
                                            onClick={onClose}
                                            className="px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-600 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
                                        >
                                            Cancel
                                        </button>
                                        <button
                                            onClick={onContinue}
                                            className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 transition-colors"
                                        >
                                            Continue Anyway
                                        </button>
                                    </div>
                                </div>
                            </Dialog.Panel>
                        </Transition.Child>
                    </div>
                </div>
            </Dialog>
        </Transition>
    );
};

export default DuplicatePatientWarningModal;