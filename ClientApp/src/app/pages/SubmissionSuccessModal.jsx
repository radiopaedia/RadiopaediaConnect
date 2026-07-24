import { Fragment } from 'react';
import { Transition, Dialog } from '@headlessui/react';

const SubmissionSuccessModal = ({
    isOpen,
    caseId,
    isAppend = false,
    onGoToMyCases,
    onAddNewCase,
    onViewCase
}) => {
    return (
        <Transition show={isOpen} as={Fragment}>
            <Dialog as="div" className="relative z-40" onClose={() => { }}>
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
                            <Dialog.Panel className="w-full max-w-md transform overflow-hidden rounded-xl bg-white dark:bg-slate-800 shadow-2xl transition-all">
                                {/* Header */}
                                <div className="bg-green-50 dark:bg-green-900/20 px-6 py-5 border-b border-green-200 dark:border-green-800">
                                    <div className="flex items-start gap-3">
                                        <div className="flex-shrink-0 w-10 h-10 rounded-full bg-green-100 dark:bg-green-900 flex items-center justify-center">
                                            <svg className="w-5 h-5 text-green-600 dark:text-green-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7" />
                                            </svg>
                                        </div>
                                        <div>
                                            <Dialog.Title className="text-lg font-bold text-green-800 dark:text-green-200">
                                                {isAppend ? 'Studies Queued' : 'Case Submitted'}
                                            </Dialog.Title>
                                            <p className="text-sm text-green-700 dark:text-green-300 mt-1">
                                                {isAppend
                                                    ? 'The additional studies have been queued for upload to the existing case.'
                                                    : 'Your upload case has been successfully submitted.'}
                                            </p>
                                        </div>
                                    </div>
                                </div>

                                {/* Case ID Reference */}
                                {caseId && (
                                    <div className="px-6 py-3 bg-slate-50 dark:bg-slate-900/30 border-b border-slate-200 dark:border-slate-700">
                                        <div className="flex items-center gap-2 text-sm text-slate-600 dark:text-slate-400">
                                            <svg className="w-4 h-4 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M7 20l4-16m2 16l4-16M6 9h14M4 15h14" />
                                            </svg>
                                            <span>Case ID:</span>
                                            <span className="font-mono font-medium text-slate-900 dark:text-white">{caseId}</span>
                                        </div>
                                    </div>
                                )}

                                {/* Navigation Options */}
                                <div className="px-6 py-5 space-y-3">
                                    <p className="text-sm text-slate-600 dark:text-slate-400 mb-4">
                                        What would you like to do next?
                                    </p>

                                    <button
                                        onClick={() => onViewCase(caseId)}
                                        className="w-full flex items-center gap-3 p-3 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-indigo-50 dark:hover:bg-indigo-900/20 hover:border-indigo-300 dark:hover:border-indigo-700 transition-colors group text-left"
                                    >
                                        <div className="flex-shrink-0 w-9 h-9 rounded-lg bg-indigo-100 dark:bg-indigo-900 flex items-center justify-center">
                                            <svg className="w-4 h-4 text-indigo-600 dark:text-indigo-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                                            </svg>
                                        </div>
                                        <div className="flex-1 min-w-0">
                                            <div className="font-medium text-slate-900 dark:text-white group-hover:text-indigo-600 dark:group-hover:text-indigo-400">
                                                View submitted case details
                                            </div>
                                            <div className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                                                Open the case drawer to review what was submitted
                                            </div>
                                        </div>
                                        <svg className="w-4 h-4 text-slate-400 group-hover:text-indigo-500 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5l7 7-7 7" />
                                        </svg>
                                    </button>

                                    <button
                                        onClick={onGoToMyCases}
                                        className="w-full flex items-center gap-3 p-3 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-indigo-50 dark:hover:bg-indigo-900/20 hover:border-indigo-300 dark:hover:border-indigo-700 transition-colors group text-left"
                                    >
                                        <div className="flex-shrink-0 w-9 h-9 rounded-lg bg-slate-100 dark:bg-slate-700 flex items-center justify-center">
                                            <svg className="w-4 h-4 text-slate-600 dark:text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
                                            </svg>
                                        </div>
                                        <div className="flex-1 min-w-0">
                                            <div className="font-medium text-slate-900 dark:text-white group-hover:text-indigo-600 dark:group-hover:text-indigo-400">
                                                Go to My Cases
                                            </div>
                                            <div className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                                                {"View your case\u0027s progress and status"}
                                            </div>
                                        </div>
                                        <svg className="w-4 h-4 text-slate-400 group-hover:text-indigo-500 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5l7 7-7 7" />
                                        </svg>
                                    </button>

                                    <button
                                        onClick={onAddNewCase}
                                        className="w-full flex items-center gap-3 p-3 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-indigo-50 dark:hover:bg-indigo-900/20 hover:border-indigo-300 dark:hover:border-indigo-700 transition-colors group text-left"
                                    >
                                        <div className="flex-shrink-0 w-9 h-9 rounded-lg bg-slate-100 dark:bg-slate-700 flex items-center justify-center">
                                            <svg className="w-4 h-4 text-slate-600 dark:text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4v16m8-8H4" />
                                            </svg>
                                        </div>
                                        <div className="flex-1 min-w-0">
                                            <div className="font-medium text-slate-900 dark:text-white group-hover:text-indigo-600 dark:group-hover:text-indigo-400">
                                                Add a new case
                                            </div>
                                            <div className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                                                Start a fresh PACS search for another case
                                            </div>
                                        </div>
                                        <svg className="w-4 h-4 text-slate-400 group-hover:text-indigo-500 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5l7 7-7 7" />
                                        </svg>
                                    </button>
                                </div>
                            </Dialog.Panel>
                        </Transition.Child>
                    </div>
                </div>
            </Dialog>
        </Transition>
    );
};

export default SubmissionSuccessModal;