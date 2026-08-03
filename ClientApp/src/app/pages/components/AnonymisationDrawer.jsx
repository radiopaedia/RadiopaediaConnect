import { useState, useEffect, Fragment } from 'react';
import { Transition } from '@headlessui/react';

// ── Data ─────────────────────────────────────────────────────────────────────
// The retained ("keep") and zeroed tag lists are NOT hard-coded here — they are
// fetched at runtime from GET /api/anonymisation/policy, which is backed by the
// same Config/dicom-allowlist.json the anonymiser uses. This guarantees the UI and
// the pipeline can never disagree about which tags are kept.
//
// Policy source: github.com/radiopaedia/dicom-anonymiser
// Our strategy is aligned with Radiopaedia's open-source anonymiser so that
// files produced here are compatible with their server-side re-anonymiser and viewer.

// Friendly headings for the DICOM group component (first 4 hex digits of the tag).
// Derived from the tag itself, so it never drifts from the allowlist contents.
const GROUP_LABELS = {
    '0008': { label: 'SOP & general', description: 'SOP class, modality, series description and other general identifiers needed to interpret the object.' },
    '0010': { label: 'Patient (non-identifying)', description: 'Patient attributes Radiopaedia deems non-identifying (e.g. sex).' },
    '0018': { label: 'Acquisition parameters', description: 'Technical scanner settings that provide clinical context without identifying the patient (CT, MR, US, DX, fluoroscopy).' },
    '0020': { label: 'Spatial geometry', description: 'Orientation, position and ordering metadata so viewers can show slices in the correct anatomical sequence.' },
    '0028': { label: 'Image pixel & display', description: 'Pixel structure, windowing, LUTs and compression values required to decode and render the image.' },
    '0040': { label: 'Dose & exposure', description: 'Radiation dose and exposure measurements (no PHI).' },
    '0054': { label: 'Nuclear medicine / PET', description: 'Energy windows, detectors, gating and decay metadata for NM/PET series.' },
    '2050': { label: 'Presentation LUT', description: 'Presentation LUT shape used for correct greyscale display.' },
    '5600': { label: 'Spectroscopy', description: 'MR spectroscopy data and phase-correction values.' },
    '7FE0': { label: 'Pixel data', description: 'The raw image bytes.' },
};

const groupOf = (tag) => (tag || '').replace(/[()]/g, '').slice(0, 4).toUpperCase();

// Build the grouped structure the UI renders from a flat list of {tag, name, group}.
const buildGroups = (keep) => {
    const byGroup = new Map();
    for (const t of keep) {
        const g = (t.group || groupOf(t.tag)).toUpperCase();
        if (!byGroup.has(g)) byGroup.set(g, []);
        byGroup.get(g).push({ tag: t.tag, name: t.description || t.alias || t.tag });
    }
    return [...byGroup.keys()].sort().map((g) => ({
        label: GROUP_LABELS[g]?.label ?? `Group ${g}`,
        description: GROUP_LABELS[g]?.description ?? '',
        tags: byGroup.get(g),
    }));
};

const REMOVED_CATEGORIES = [
    { label: 'Patient demographics (full)',        examples: 'Age (beyond year-level), weight, address, ethnicity, medical record numbers' },
    { label: 'Clinicians',                         examples: 'Performing physician, reading physician, operator name' },
    { label: 'Institution details',                examples: 'Hospital name, address, department, station name' },
    { label: 'Study & accession identifiers',      examples: 'Study description, requested procedure, scheduled procedure step' },
    { label: 'Protocol & procedure info',          examples: 'Protocol name, performed procedure step' },
    { label: 'All private tags',                   examples: 'Vendor-specific tags (odd group numbers), content is uncontrolled' },
    { label: 'Overlay groups (60xx)',               examples: 'Can contain burned-in annotations with patient text' },
    { label: 'Sequence-wrapped equivalents',        examples: 'VOI LUT / Modality LUT sequences; scalar Window/Rescale values above are sufficient' },
    { label: 'All UIDs not listed above',           examples: 'Referenced SOP UIDs, Concatenation UIDs, etc.' },
];

// ── Shared content ────────────────────────────────────────────────────────────

export const AnonymisationContent = () => {
    const [tagGroups, setTagGroups] = useState([]);
    const [zeroedTags, setZeroedTags] = useState([]);
    const [status, setStatus] = useState('loading'); // 'loading' | 'ready' | 'error'
    const [openGroups, setOpenGroups] = useState([]);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const res = await fetch('/api/anonymisation/policy');
                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                const data = await res.json();
                if (cancelled) return;
                const groups = buildGroups(data.keep ?? []);
                setTagGroups(groups);
                setZeroedTags([
                    ...(data.zeroed ?? []).map(z => ({ ...z, note: 'Present but empty' })),
                    ...(data.removed ?? []).map(r => ({ ...r, note: 'Set to "REMOVED"' })),
                ]);
                setOpenGroups(groups.map((_, i) => i));
                setStatus('ready');
            } catch {
                if (!cancelled) setStatus('error');
            }
        })();
        return () => { cancelled = true; };
    }, []);

    const toggle = (i) =>
        setOpenGroups(prev => prev.includes(i) ? prev.filter(x => x !== i) : [...prev, i]);

    return (
        <div className="space-y-7">

            {/* Intro */}
            <div className="space-y-2 text-sm text-slate-600 dark:text-slate-400 leading-relaxed">
                <p>
                    When uploading as native DICOM, RadiopaediaConnect anonymises every file before it
                    leaves this system. Our strategy is aligned with the open-source{' '}
                    <a href="https://github.com/radiopaedia/dicom-anonymiser" target="_blank" rel="noreferrer"
                        className="underline text-indigo-600 dark:text-indigo-400">
                        Radiopaedia DICOM Anonymiser
                    </a>{' '}
                    so that files are compatible with Radiopaedia&apos;s server-side re-anonymiser and viewer.
                </p>
                <p>
                    We use a strict <strong className="text-slate-800 dark:text-slate-200">allowlist</strong>:
                    a blank DICOM dataset is created and only the tags listed below are copied across.
                    Everything else, including all vendor-specific private tags, is silently discarded.
                </p>
                <p>
                    Study, Series, and Frame of Reference UIDs are replaced using Radiopaedia&apos;s
                    SHA-512 hashing algorithm, the same deterministic hash their server-side validator
                    uses to confirm anonymisation. UIDs are consistent <em>within</em> a series (so
                    viewers can reconstruct multi-slice stacks and MPR views) but bear no relation to the
                    originals. SOP Instance UID{' '}
                    <code className="font-mono bg-slate-100 dark:bg-slate-800 px-0.5 rounded text-[11px]">
                        (0008,0018)
                    </code>{' '}
                    is intentionally absent from the dataset; Radiopaedia&apos;s server rejects files
                    where it is present, regardless of value.
                </p>
            </div>

            {/* Retained tags */}
            <div>
                <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500 mb-2">
                    Tags retained (verbatim copy)
                    {status === 'ready' && (
                        <span className="ml-2 normal-case font-normal text-slate-400 dark:text-slate-500">
                            {tagGroups.reduce((n, g) => n + g.tags.length, 0)} tags
                        </span>
                    )}
                </h3>

                {status === 'loading' && (
                    <p className="text-xs text-slate-400 dark:text-slate-500 italic">Loading anonymisation policy…</p>
                )}
                {status === 'error' && (
                    <p className="text-xs text-red-600 dark:text-red-400">
                        Could not load the anonymisation policy from the server.
                    </p>
                )}

                <div className="space-y-1.5">
                    {tagGroups.map((group, i) => (
                        <div key={group.label}
                            className="border border-slate-200 dark:border-slate-700 rounded-lg overflow-hidden">
                            <button
                                onClick={() => toggle(i)}
                                className="w-full flex items-center justify-between px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800/60 hover:bg-slate-100 dark:hover:bg-slate-700/50 transition-colors text-left"
                            >
                                <div className="flex items-center gap-2">
                                    <span className="text-sm font-semibold text-slate-800 dark:text-slate-200">
                                        {group.label}
                                    </span>
                                    <span className="text-[11px] text-slate-400 dark:text-slate-500">
                                        {group.tags.length} tags
                                    </span>
                                </div>
                                <svg className={`w-4 h-4 text-slate-400 transition-transform ${openGroups.includes(i) ? 'rotate-180' : ''}`}
                                    fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                                </svg>
                            </button>

                            {openGroups.includes(i) && (
                                <div className="px-3.5 py-3 bg-white dark:bg-slate-900/40 border-t border-slate-100 dark:border-slate-700/50">
                                    <p className="text-xs text-slate-400 dark:text-slate-500 mb-2.5 italic">
                                        {group.description}
                                    </p>
                                    <table className="w-full text-xs">
                                        <thead>
                                            <tr className="text-left text-[10px] uppercase tracking-wide text-slate-400 dark:text-slate-500">
                                                <th className="pb-1.5 font-medium w-28">Tag</th>
                                                <th className="pb-1.5 font-medium">Name</th>
                                            </tr>
                                        </thead>
                                        <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                                            {group.tags.map(t => (
                                                <tr key={t.tag}>
                                                    <td className="py-1 font-mono text-slate-400 dark:text-slate-500 pr-3 whitespace-nowrap">
                                                        {t.tag}
                                                    </td>
                                                    <td className="py-1 text-slate-700 dark:text-slate-300">
                                                        {t.name}
                                                    </td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            )}
                        </div>
                    ))}
                </div>
            </div>

            {/* Zeroed PHI tags */}
            <div>
                <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500 mb-1">
                    Tags overwritten (present, value replaced)
                </h3>
                <p className="text-xs text-slate-400 dark:text-slate-500 mb-2 italic">
                    DICOM IOD rules require these tags to exist even when their value is unknown.
                    Removing them entirely fails strict validators, so we apply the same
                    &ldquo;replace&rdquo; action as Radiopaedia&apos;s anonymiser: most are written as
                    empty strings, while the equipment tags their policy marks as type-1 are written
                    as the literal <span className="font-mono">REMOVED</span>.
                </p>
                <div className="rounded-lg border border-slate-200 dark:border-slate-700 divide-y divide-slate-100 dark:divide-slate-700/50 overflow-hidden">
                    {zeroedTags.map(t => (
                        <div key={t.tag} className="px-3.5 py-2 flex gap-3 items-baseline bg-white dark:bg-slate-900/20">
                            <span className="font-mono text-[11px] text-slate-400 dark:text-slate-500 w-28 flex-shrink-0">{t.tag}</span>
                            <span className="text-xs text-slate-700 dark:text-slate-300 flex-1">{t.name}</span>
                            <span className="text-xs text-slate-400 dark:text-slate-500 italic">{t.note}</span>
                        </div>
                    ))}
                </div>
            </div>

            {/* Removed */}
            <div>
                <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500 mb-2">
                    Always removed
                </h3>
                <div className="rounded-lg border border-red-100 dark:border-red-900/30 divide-y divide-red-100 dark:divide-red-900/30 overflow-hidden">
                    {REMOVED_CATEGORIES.map(cat => (
                        <div key={cat.label} className="px-3.5 py-2 flex gap-3 items-baseline bg-red-50/60 dark:bg-red-950/20">
                            <span className="text-xs font-semibold text-red-700 dark:text-red-400 w-52 flex-shrink-0">
                                {cat.label}
                            </span>
                            <span className="text-xs text-red-600/80 dark:text-red-400/70 leading-relaxed">
                                {cat.examples}
                            </span>
                        </div>
                    ))}
                </div>
            </div>

            {/* Redaction / PNG fallback note */}
            <div className="bg-amber-50 dark:bg-amber-950/20 border border-amber-200 dark:border-amber-800/40 rounded-lg px-4 py-3">
                <p className="text-xs text-amber-800 dark:text-amber-300 leading-relaxed">
                    <strong>When PNG is used instead:</strong> If image redaction is applied, or if a
                    multi-frame series is partially culled, that series is uploaded as rendered PNG images
                    rather than native DICOM. Tag-level anonymisation cannot remove PHI burned into pixel
                    data (e.g. patient name overlaid on the image), and partial frame extraction from
                    multi-frame files is not supported.
                </p>
            </div>

            {/* Source reference */}
            <p className="text-xs text-slate-400 dark:text-slate-500">
                Tag lists above are served live from{' '}
                <code className="font-mono bg-slate-100 dark:bg-slate-800 px-1 py-0.5 rounded">
                    Config/dicom-allowlist.json
                </code>
                {' '}via{' '}
                <code className="font-mono bg-slate-100 dark:bg-slate-800 px-1 py-0.5 rounded">
                    /api/anonymisation/policy
                </code>
                , the same source the anonymiser uses. Policy reference:{' '}
                <a href="https://github.com/radiopaedia/dicom-anonymiser" target="_blank" rel="noreferrer"
                    className="underline text-indigo-600 dark:text-indigo-400">
                    github.com/radiopaedia/dicom-anonymiser
                </a>
            </p>

        </div>
    );
};

// ── Drawer wrapper ────────────────────────────────────────────────────────────

const AnonymisationDrawer = ({ isOpen, onClose }) => {
    // Lock body scroll while the drawer is open so only the drawer scrolls.
    useEffect(() => {
        if (isOpen) {
            document.body.style.overflow = 'hidden';
        }
        return () => {
            document.body.style.overflow = '';
        };
    }, [isOpen]);

    return (
    <Transition show={isOpen} as={Fragment}>
        <div className="fixed inset-0 overflow-hidden z-50">

            {/* Backdrop */}
            <Transition.Child
                as={Fragment}
                enter="ease-out duration-300" enterFrom="opacity-0" enterTo="opacity-100"
                leave="ease-in duration-200" leaveFrom="opacity-100" leaveTo="opacity-0"
            >
                <div className="absolute inset-0 bg-black/50" onClick={onClose} />
            </Transition.Child>

            {/* Panel */}
            <div className="fixed inset-y-0 right-0 flex max-w-full pl-10">
                <Transition.Child
                    as={Fragment}
                    enter="transform transition ease-in-out duration-300"
                    enterFrom="translate-x-full" enterTo="translate-x-0"
                    leave="transform transition ease-in-out duration-200"
                    leaveFrom="translate-x-0" leaveTo="translate-x-full"
                >
                    <div className="w-screen max-w-lg">
                        <div className="flex h-full flex-col bg-white dark:bg-slate-800 shadow-xl">

                            {/* Header */}
                            <div className="px-6 py-4 border-b border-slate-200 dark:border-slate-700 flex items-center justify-between bg-slate-50 dark:bg-slate-900/50 flex-shrink-0">
                                <div>
                                    <h2 className="text-base font-bold text-slate-900 dark:text-white">
                                        DICOM Anonymisation
                                    </h2>
                                    <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                                        What we keep, what we zero, and what we remove.
                                    </p>
                                </div>
                                <button
                                    onClick={onClose}
                                    className="rounded-md p-2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
                                >
                                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                                    </svg>
                                </button>
                            </div>

                            {/* Scrollable content */}
                            <div className="flex-1 overflow-y-auto px-6 py-5">
                                <AnonymisationContent />
                            </div>

                        </div>
                    </div>
                </Transition.Child>
            </div>

        </div>
    </Transition>
    );
};

export default AnonymisationDrawer;
