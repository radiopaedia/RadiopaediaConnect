import { useState, useEffect } from 'react';
import CasesTable from './CasesTable';

const AllCasesTab = () => {
    const [cases, setCases] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);

    useEffect(() => { fetchCases(); }, []);

    const fetchCases = async () => {
        setLoading(true);
        setError(null);
        try {
            const res = await fetch('/api/cases/all-cases');
            if (!res.ok) throw new Error('Failed to fetch cases');
            setCases(await res.json());
        } catch (err) {
            setError(err.message);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="space-y-2">
            <div className="mb-4">
                <h2 className="text-lg font-semibold">All Cases</h2>
                <p className="text-sm text-slate-500 dark:text-slate-400">Cases submitted by all users.</p>
            </div>
            <CasesTable
                cases={cases}
                loading={loading}
                error={error}
                onRefresh={fetchCases}
                showUserColumn
                adminMode
            />
        </div>
    );
};

export default AllCasesTab;
