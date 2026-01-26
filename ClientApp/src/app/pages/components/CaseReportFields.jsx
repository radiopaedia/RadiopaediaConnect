
const CaseReportFields = ({ formData, onChange }) => {
    return (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 animate-fade-in-up">
            
            {/* Card 1: Findings */}
            <div className="bg-white dark:bg-slate-800 shadow rounded-lg border border-slate-200 dark:border-slate-700 p-6 flex flex-col">
                <div className="mb-4 flex items-center justify-between">
                    <h3 className="text-lg font-bold text-slate-900 dark:text-white">
                        Findings
                    </h3>
                    
                </div>
                
                <div className="flex-1">
                    <label htmlFor="findings" className="sr-only">Findings</label>
                    <textarea
                        id="findings"
                        name="findings"
                        value={formData.findings}
                        onChange={onChange}
                        placeholder="Describe the radiological findings..."
                        className="w-full h-full min-h-[200px] rounded-md border-slate-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 dark:bg-slate-900 dark:border-slate-600 dark:text-white sm:text-sm py-3 px-4 border resize-none"
                    />
                </div>
                <p className="mt-2 text-xs text-slate-400">
                    Describe the relevant imaging features.
                </p>
            </div>

            {/* Card 2: Case Discussion */}
            <div className="bg-white dark:bg-slate-800 shadow rounded-lg border border-slate-200 dark:border-slate-700 p-6 flex flex-col">
                <div className="mb-4 flex items-center justify-between">
                    <h3 className="text-lg font-bold text-slate-900 dark:text-white">
                        Case Discussion
                    </h3>
                    
                </div>

                <div className="flex-1">
                    <label htmlFor="discussion" className="sr-only">Case Discussion</label>
                    <textarea
                        id="discussion"
                        name="discussion"
                        value={formData.discussion}
                        onChange={onChange}
                        placeholder="Discuss the diagnosis, differential, and etiology..."
                        className="w-full h-full min-h-[200px] rounded-md border-slate-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 dark:bg-slate-900 dark:border-slate-600 dark:text-white sm:text-sm py-3 px-4 border resize-none"
                    />
                </div>
                <p className="mt-2 text-xs text-slate-400">
                    Discuss the diagnosis.
                </p>
            </div>

        </div>
    );
};

export default CaseReportFields;