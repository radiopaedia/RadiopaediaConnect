import { useState, useEffect } from 'react';

const PacsSearchForm = ({ onSearch, loading }) => {
    const [nodes, setNodes] = useState([]);
    const [error, setError] = useState(''); // [New] Error state

    const [formData, setFormData] = useState({
        patientName: '',
        patientId: '',
        accessionNumber: '',
        remoteNodeName: ''
    });

    // 1. Fetch available PACS nodes on mount
    useEffect(() => {
        const fetchNodes = async () => {
            try {
                const res = await fetch('/api/dicom/nodes');
                if (res.ok) {
                    const data = await res.json();
                    setNodes(data);

                    // Check for auto-search params
                    const autoAccession = localStorage.getItem('rp_auto_accession');
                    const autoNode = localStorage.getItem('rp_auto_node');
                    
                    // Clear them so we don't auto-search again on refresh unless re-navigated
                    if (autoAccession) localStorage.removeItem('rp_auto_accession');
                    if (autoNode) localStorage.removeItem('rp_auto_node');

                    let targetNodeName = '';

                    if (data && data.length > 0) {
                        // Default to first node
                        targetNodeName = data[0].name;

                        // If user specified a node, try to find it
                        if (autoNode) {
                            const found = data.find(n => n.name === autoNode);
                            if (found) targetNodeName = found.name;
                        }
                        
                        setFormData(prev => ({ 
                            ...prev, 
                            remoteNodeName: targetNodeName,
                            accessionNumber: autoAccession || prev.accessionNumber
                        }));
                    }

                    // If we have an accession number, trigger search automatically
                    if (autoAccession && targetNodeName) {
                        onSearch({
                            patientName: '',
                            patientId: '',
                            accessionNumber: autoAccession,
                            remoteNodeName: targetNodeName
                        });
                    }
                }
            } catch (err) {
                console.error("Failed to load PACS nodes", err);
            }
        };
        fetchNodes();
    }, []); // Empty dependency array means this runs once on mount

    const handleChange = (e) => {
        const { name, value } = e.target;
        setFormData(prev => ({ ...prev, [name]: value }));
        if (error) setError(''); // Clear error on type
    };

    const handleSubmit = (e) => {
        e.preventDefault();

        // [New] Validation Logic
        const hasCriteria =
            formData.patientName.trim() !== '' ||
            formData.patientId.trim() !== '' ||
            formData.accessionNumber.trim() !== '';

        if (!hasCriteria) {
            setError('Please enter at least one search filter (Name, ID, or Accession).');
            return;
        }

        onSearch(formData);
    };

    return (
        <div className="bg-white dark:bg-slate-800 shadow rounded-lg border border-slate-200 dark:border-slate-700 p-6 sticky top-6">
            <h2 className="text-lg font-semibold text-slate-800 dark:text-white mb-4 flex items-center">
                <svg className="w-5 h-5 mr-2 text-indigo-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                PACS Query
            </h2>

            <form onSubmit={handleSubmit} className="space-y-4">
                {/* Error Message */}
                {error && (
                    <div className="bg-red-50 text-red-600 text-sm p-3 rounded-md border border-red-200 animate-pulse">
                        {error}
                    </div>
                )}

                {/* PACS Node Selector */}
                <div>
                    <label className="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-1">
                        Source PACS
                    </label>
                    <select
                        name="remoteNodeName"
                        value={formData.remoteNodeName}
                        onChange={handleChange}
                        className="w-full rounded-md border-slate-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 dark:bg-slate-900 dark:border-slate-600 dark:text-white sm:text-sm py-2 px-3 border"
                    >
                        {nodes.map((node) => (
                            <option key={node.name} value={node.name}>
                                {node.label || node.name}
                            </option>
                        ))}
                    </select>
                </div>

                {/* Patient ID */}
                <div>
                    <label className="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-1">
                        Patient ID (MRN)
                    </label>
                    <input
                        type="text"
                        name="patientId"
                        value={formData.patientId}
                        onChange={handleChange}
                        placeholder="e.g. 1234567"
                        className="w-full rounded-md border-slate-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 dark:bg-slate-900 dark:border-slate-600 dark:text-white sm:text-sm py-2 px-3 border"
                    />
                </div>

                {/* Accession Number */}
                <div>
                    <label className="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-1">
                        Accession Number
                    </label>
                    <input
                        type="text"
                        name="accessionNumber"
                        value={formData.accessionNumber}
                        onChange={handleChange}
                        placeholder="e.g. ACC-2023-001"
                        className="w-full rounded-md border-slate-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 dark:bg-slate-900 dark:border-slate-600 dark:text-white sm:text-sm py-2 px-3 border"
                    />
                </div>

                {/* Patient Name */}
                <div>
                    <label className="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-1">
                        Patient Name
                    </label>
                    <input
                        type="text"
                        name="patientName"
                        value={formData.patientName}
                        onChange={handleChange}
                        placeholder="Last^First"
                        className="w-full rounded-md border-slate-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 dark:bg-slate-900 dark:border-slate-600 dark:text-white sm:text-sm py-2 px-3 border"
                    />
                </div>

                <div className="pt-4">
                    <button
                        type="submit"
                        disabled={loading}
                        className={`w-full flex justify-center py-2 px-4 border border-transparent rounded-md shadow-sm text-sm font-medium text-white transition-colors
                            ${loading
                                ? 'bg-indigo-400 cursor-not-allowed'
                                : 'bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500'
                            }`}
                    >
                        {loading ? 'Searching PACS...' : 'Search Studies'}
                    </button>
                </div>
            </form>
        </div>
    );
};

export default PacsSearchForm;