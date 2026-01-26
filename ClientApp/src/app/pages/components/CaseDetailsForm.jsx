const CaseDetailsForm = ({ formData, onChange, errors = {}, systemMap = {}, certaintyOptions = [] }) => {

    const InputLabel = ({ label, name }) => (
        <label className="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-1">
            {label} {['title', 'system', 'presentation', 'age', 'sex'].includes(name) && <span className="text-red-500">*</span>}
        </label>
    );

    const ErrorMsg = ({ error }) => (
        error ? <p className="text-xs text-red-500 mt-1">{error}</p> : null
    );

    // Added "h-full flex flex-col" to the main container
    return (
        <div className="bg-white dark:bg-slate-800 shadow rounded-lg border border-slate-200 dark:border-slate-700 p-6 h-full flex flex-col">
            <div className="mb-4 flex-none">
                <h2 className="text-lg font-bold text-slate-900 dark:text-white">Case Details</h2>
            </div>

            {/* Content Container: flex-1 to fill remaining space */}
            <div className="flex-1 flex flex-col space-y-4">

                {/* 1. Title */}
                <div>
                    <InputLabel label="Title" name="title" />
                    <input
                        type="text"
                        name="title"
                        value={formData.title}
                        onChange={onChange}
                        className={`w-full rounded-md border py-2 px-3 shadow-sm ${errors.title ? 'border-red-500' : 'border-slate-300 dark:border-slate-600 dark:bg-slate-900'}`}
                    />
                    <ErrorMsg error={errors.title} />
                </div>

                {/* 2. Age & Sex */}
                <div className="grid grid-cols-2 gap-4">
                    <div>
                        <InputLabel label="Age" name="age" />
                        <input
                            type="text"
                            name="age"
                            value={formData.age}
                            onChange={onChange}
                            placeholder="e.g. 5 years, 2 months"
                            className={`w-full rounded-md border py-2 px-3 shadow-sm ${errors.age ? 'border-red-500' : 'border-slate-300 dark:border-slate-600 dark:bg-slate-900'}`}
                        />
                        <ErrorMsg error={errors.age} />
                    </div>
                    <div>
                        <InputLabel label="Sex" name="sex" />
                        <select
                            name="sex"
                            value={formData.sex}
                            onChange={onChange}
                            className={`w-full rounded-md border py-2 px-3 shadow-sm ${errors.sex ? 'border-red-500' : 'border-slate-300 dark:border-slate-600 dark:bg-slate-900'}`}
                        >
                            <option value="">Select</option>
                            <option value="male">Male</option>
                            <option value="female">Female</option>
                            <option value="other">Other</option>
                        </select>
                        <ErrorMsg error={errors.sex} />
                    </div>
                </div>

                {/* 3. System */}
                <div>
                    <InputLabel label="System" name="system" />
                    <select
                        name="system"
                        value={formData.system}
                        onChange={onChange}
                        className={`w-full rounded-md border py-2 px-3 shadow-sm ${errors.system ? 'border-red-500' : 'border-slate-300 dark:border-slate-600 dark:bg-slate-900'}`}
                    >
                        <option value="">Select System</option>
                        {/* 2. Dynamically map options from the prop */}
                        {Object.keys(systemMap).sort().map((systemName) => (
                            <option key={systemName} value={systemName}>
                                {systemName}
                            </option>
                        ))}
                    </select>
                    <ErrorMsg error={errors.system} />
                </div>

                {/* 4. Diagnostic Certainty */}
                <div>
                    <InputLabel label="Diagnostic Certainty" name="diagnostic_certainty" />
                    <div className="flex gap-2">
                        <select
                            name="diagnostic_certainty"
                            value={formData.diagnostic_certainty}
                            onChange={onChange}
                            className="w-full rounded-md border-slate-300 dark:bg-slate-900 dark:border-slate-600 py-2 px-3 border shadow-sm"
                        >
                            <option value="">Select Certainty (Optional)</option>
                            {certaintyOptions.map(c => <option key={c.id} value={c.id}>{c.label}</option>)}
                        </select>
                        <a
                            href="https://radiopaedia.org/articles/diagnostic-certainty-2"
                            target="_blank"
                            rel="noopener noreferrer"
                            className="flex-none flex items-center justify-center text-slate-400 hover:text-indigo-600 dark:text-slate-500 dark:hover:text-indigo-400 transition-colors"
                            title="Help: Diagnostic Certainty Definitions"
                        >
                            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-6 h-6">
                                <path strokeLinecap="round" strokeLinejoin="round" d="M9.879 7.519c1.171-1.025 3.071-1.025 4.242 0 1.172 1.025 1.172 2.687 0 3.712-.203.179-.43.326-.67.442-.745.361-1.45.999-1.45 1.827v.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 5.25h.008v.008H12v-.008Z" />
                            </svg>
                        </a>
                    </div>
                </div>

                {/* 5. Presentation (Flexible Height) */}
                <div className="flex-1 flex flex-col min-h-[120px]">
                    <InputLabel label="Presentation" name="presentation" />
                    <textarea
                        name="presentation"
                        value={formData.presentation}
                        onChange={onChange}
                        // Removed fixed 'rows' to allow flex-grow control
                        className={`w-full flex-1 rounded-md border py-2 px-3 shadow-sm resize-none ${errors.presentation ? 'border-red-500' : 'border-slate-300 dark:border-slate-600 dark:bg-slate-900'}`}
                        placeholder="Describe the clinical presentation..."
                    />
                    <ErrorMsg error={errors.presentation} />
                </div>

            </div>
        </div>
    );
};

export default CaseDetailsForm;