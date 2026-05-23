import { useState, useEffect, Fragment } from 'react';
import { Transition } from '@headlessui/react';

// ── Data ─────────────────────────────────────────────────────────────────────
// Mirrors the allowlist in Services/Dicom/DicomAnonymizer.cs exactly.
// Keep these two files in sync when the tag list changes.
//
// Policy source: github.com/radiopaedia/dicom-anonymiser (AGPL-3.0)
// Our strategy is aligned with Radiopaedia's open-source anonymiser so that
// files produced here are compatible with their server-side re-anonymiser and viewer.

const TAG_GROUPS = [
    {
        label: 'Pixel data',
        description: 'The raw image bytes and every structural tag required to decode them correctly, including planar configuration for colour images.',
        tags: [
            { tag: '(0008,0016)', name: 'SOP Class UID',              note: 'Image type: CT, MR, US, etc. Unchanged.' },
            { tag: '(0028,0002)', name: 'Samples Per Pixel' },
            { tag: '(0028,0004)', name: 'Photometric Interpretation' },
            { tag: '(0028,0006)', name: 'Planar Configuration',        note: 'Required for RGB / colour images' },
            { tag: '(0028,0010)', name: 'Rows' },
            { tag: '(0028,0011)', name: 'Columns' },
            { tag: '(0028,0034)', name: 'Pixel Aspect Ratio' },
            { tag: '(0028,0100)', name: 'Bits Allocated' },
            { tag: '(0028,0101)', name: 'Bits Stored' },
            { tag: '(0028,0102)', name: 'High Bit' },
            { tag: '(0028,0103)', name: 'Pixel Representation' },
            { tag: '(0028,0106)', name: 'Smallest Image Pixel Value' },
            { tag: '(0028,0107)', name: 'Largest Image Pixel Value' },
            { tag: '(0028,0120)', name: 'Pixel Padding Value' },
            { tag: '(0028,0121)', name: 'Pixel Padding Range Limit' },
            { tag: '(0028,0300)', name: 'Quality Control Image' },
            { tag: '(0028,0301)', name: 'Burned In Annotation',        note: 'Flag only; pixel PHI still requires PNG fallback' },
            { tag: '(0028,0008)', name: 'Number of Frames',            note: 'Multi-frame' },
            { tag: '(0028,0009)', name: 'Frame Increment Pointer',     note: 'Multi-frame' },
            { tag: '(7FE0,0010)', name: 'Pixel Data',                  note: 'The actual image bytes' },
        ],
    },
    {
        label: 'Display & windowing',
        description: 'Windowing, LUT, and lossy-compression values needed to render the image correctly in a DICOM viewer.',
        tags: [
            { tag: '(0028,1050)', name: 'Window Center' },
            { tag: '(0028,1051)', name: 'Window Width' },
            { tag: '(0028,1055)', name: 'Window Center Width Explanation' },
            { tag: '(0028,1056)', name: 'VOI LUT Function' },
            { tag: '(0028,1052)', name: 'Rescale Intercept',           note: 'CT HU conversion' },
            { tag: '(0028,1053)', name: 'Rescale Slope' },
            { tag: '(0028,1054)', name: 'Rescale Type' },
            { tag: '(0028,1040)', name: 'Pixel Intensity Relationship' },
            { tag: '(0028,1041)', name: 'Pixel Intensity Relationship Sign' },
            { tag: '(2050,0020)', name: 'Presentation LUT Shape' },
            { tag: '(0028,2110)', name: 'Lossy Image Compression' },
            { tag: '(0028,2112)', name: 'Lossy Image Compression Ratio' },
            { tag: '(0028,2114)', name: 'Lossy Image Compression Method' },
            { tag: '(0028,1101–03)', name: 'Palette LUT Descriptors',  note: 'Colour-mapped images' },
            { tag: '(0028,1201–03)', name: 'Palette LUT Data',         note: 'Colour-mapped images' },
            { tag: '(0028,1221–23)', name: 'Segmented Palette LUT Data', note: 'Colour-mapped images' },
            { tag: '(0028,1300)', name: 'Breast Implant Present' },
        ],
    },
    {
        label: 'Spatial geometry',
        description: 'Orientation, spacing, and frame-of-reference metadata that allows viewers to display slices in the correct anatomical position and reconstruct multi-planar views.',
        tags: [
            { tag: '(0020,0052)', name: 'Frame of Reference UID',      note: 'SHA-512 hashed, consistent within a series; required for CT/MR IOD' },
            { tag: '(0020,0037)', name: 'Image Orientation (Patient)' },
            { tag: '(0020,0032)', name: 'Image Position (Patient)' },
            { tag: '(0028,0030)', name: 'Pixel Spacing' },
            { tag: '(0018,0050)', name: 'Slice Thickness' },
            { tag: '(0020,1041)', name: 'Slice Location' },
            { tag: '(0018,0088)', name: 'Spacing Between Slices' },
            { tag: '(0018,1164)', name: 'Imager Pixel Spacing' },
            { tag: '(0020,0013)', name: 'Instance Number',             note: 'Frame ordering' },
            { tag: '(0020,0012)', name: 'Acquisition Number' },
            { tag: '(0020,0011)', name: 'Series Number' },
            { tag: '(0020,0060)', name: 'Laterality',                  note: 'Left / Right, not patient-identifying' },
            { tag: '(0020,0020)', name: 'Patient Orientation' },
            { tag: '(0018,5100)', name: 'Patient Position',            note: 'e.g. HFS, HFP - scanner table orientation' },
            { tag: '(0020,1040)', name: 'Position Reference Indicator' },
            { tag: '(0020,1002)', name: 'Images in Acquisition' },
        ],
    },
    {
        label: 'Acquisition parameters',
        description: 'Technical scanner settings that provide clinical context without identifying the patient. Covers CT, MR, US, DX, and fluoroscopy parameters.',
        tags: [
            { tag: '(0008,0060)', name: 'Modality' },
            { tag: '(0008,0008)', name: 'Image Type' },
            { tag: '(0008,103E)', name: 'Series Description' },
            { tag: '(0018,0015)', name: 'Body Part Examined',          note: 'e.g. CHEST, HEAD' },
            { tag: '(0018,0010)', name: 'Contrast/Bolus Agent' },
            { tag: '(0018,1048)', name: 'Contrast/Bolus Route' },
            { tag: '(0018,0022)', name: 'Scan Options',                note: 'CT' },
            { tag: '(0018,0020)', name: 'Scanning Sequence',           note: 'MR' },
            { tag: '(0018,0021)', name: 'Sequence Variant',            note: 'MR' },
            { tag: '(0018,0023)', name: 'MR Acquisition Type' },
            { tag: '(0018,0060)', name: 'KVP',                         note: 'CT tube voltage' },
            { tag: '(0018,0080)', name: 'Repetition Time (TR)',         note: 'MR' },
            { tag: '(0018,0081)', name: 'Echo Time (TE)',               note: 'MR' },
            { tag: '(0018,0082)', name: 'Inversion Time (TI)',          note: 'MR' },
            { tag: '(0018,0083)', name: 'Number of Averages',           note: 'MR' },
            { tag: '(0018,0084)', name: 'Imaging Frequency',            note: 'MR' },
            { tag: '(0018,0085)', name: 'Imaged Nucleus',               note: 'MR' },
            { tag: '(0018,0086)', name: 'Echo Number(s)',               note: 'MR' },
            { tag: '(0018,0087)', name: 'Magnetic Field Strength',      note: 'MR' },
            { tag: '(0018,0089)', name: 'Number of Phase Encoding Steps' },
            { tag: '(0018,0090)', name: 'Data Collection Diameter' },
            { tag: '(0018,0091)', name: 'Echo Train Length',            note: 'MR' },
            { tag: '(0018,0093)', name: 'Percent Sampling' },
            { tag: '(0018,0094)', name: 'Percent Phase Field of View' },
            { tag: '(0018,0095)', name: 'Pixel Bandwidth' },
            { tag: '(0018,1050)', name: 'Spatial Resolution' },
            { tag: '(0018,1063)', name: 'Frame Time',                   note: 'Cine' },
            { tag: '(0018,1065)', name: 'Frame Time Vector',            note: 'Cine' },
            { tag: '(0028,6010)', name: 'Representative Frame Number' },
            { tag: '(0018,1088)', name: 'Heart Rate' },
            { tag: '(0018,1090)', name: 'Cardiac Number of Images' },
            { tag: '(0018,1094)', name: 'Trigger Window' },
            { tag: '(0018,1100)', name: 'Reconstruction Diameter',      note: 'CT' },
            { tag: '(0018,1110)', name: 'Distance Source to Detector' },
            { tag: '(0018,1111)', name: 'Distance Source to Patient' },
            { tag: '(0018,1114)', name: 'Est. Radiographic Magnification' },
            { tag: '(0018,1120)', name: 'Gantry/Detector Tilt',         note: 'CT' },
            { tag: '(0018,1130)', name: 'Table Height' },
            { tag: '(0018,1140)', name: 'Rotation Direction' },
            { tag: '(0018,1150)', name: 'Exposure Time' },
            { tag: '(0018,1151)', name: 'X-Ray Tube Current' },
            { tag: '(0018,1152)', name: 'Exposure' },
            { tag: '(0018,1160)', name: 'Filter Type' },
            { tag: '(0018,1190)', name: 'Focal Spot(s)' },
            { tag: '(0018,1210)', name: 'Convolution Kernel',           note: 'CT reconstruction' },
            { tag: '(0018,1314)', name: 'Flip Angle',                   note: 'MR' },
            { tag: '(0018,1315)', name: 'Variable Flip Angle Flag',     note: 'MR' },
            { tag: '(0018,1316)', name: 'SAR',                          note: 'MR specific absorption rate' },
            { tag: '(0018,9037)', name: 'Cardiac Synchronization Technique' },
            { tag: '(0018,9085)', name: 'Cardiac Signal Source' },
            { tag: '(0018,9306)', name: 'Single Collimation Width',     note: 'CT' },
            { tag: '(0018,9307)', name: 'Total Collimation Width',      note: 'CT' },
            { tag: '(0018,9309)', name: 'Table Speed',                  note: 'CT' },
            { tag: '(0018,9310)', name: 'Table Feed per Rotation',      note: 'CT' },
            { tag: '(0018,9311)', name: 'Spiral Pitch Factor',          note: 'CT' },
            { tag: '(0018,9323)', name: 'Exposure Modulation Type',     note: 'CT' },
            { tag: '(0018,9345)', name: 'CTDIvol',                      note: 'CT dose index' },
            { tag: '(0040,0314)', name: 'Half Value Layer' },
            { tag: '(0040,0316)', name: 'Organ Dose' },
        ],
    },
];

const ZEROED_TAGS = [
    { tag: '(0010,0010)', name: 'Patient Name',              note: 'Present but empty' },
    { tag: '(0010,0020)', name: 'Patient ID',                note: 'Present but empty' },
    { tag: '(0010,0030)', name: 'Patient Birth Date',        note: 'Present but empty' },
    { tag: '(0008,0020)', name: 'Study Date',                note: 'Present but empty' },
    { tag: '(0008,0030)', name: 'Study Time',                note: 'Present but empty' },
    { tag: '(0008,0050)', name: 'Accession Number',          note: 'Present but empty' },
    { tag: '(0008,0090)', name: 'Referring Physician Name',  note: 'Present but empty' },
    { tag: '(0020,0010)', name: 'Study ID',                  note: 'Present but empty' },
    { tag: '(0008,0070)', name: 'Manufacturer',              note: 'Present but empty' },
];

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
    const [openGroups, setOpenGroups] = useState(() => TAG_GROUPS.map((_, i) => i));
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
                </h3>
                <div className="space-y-1.5">
                    {TAG_GROUPS.map((group, i) => (
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
                                                <th className="pb-1.5 font-medium text-right">Note</th>
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
                                                    <td className="py-1 text-right text-slate-400 dark:text-slate-500 italic pl-3">
                                                        {t.note ?? ''}
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
                    Tags zeroed (present but empty)
                </h3>
                <p className="text-xs text-slate-400 dark:text-slate-500 mb-2 italic">
                    DICOM IOD rules require these tags to exist even when their value is unknown.
                    Removing them entirely fails strict validators. We write them as empty strings, the same
                    &ldquo;replace&rdquo; action used by Radiopaedia&apos;s anonymiser.
                </p>
                <div className="rounded-lg border border-slate-200 dark:border-slate-700 divide-y divide-slate-100 dark:divide-slate-700/50 overflow-hidden">
                    {ZEROED_TAGS.map(t => (
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
                Implemented in{' '}
                <code className="font-mono bg-slate-100 dark:bg-slate-800 px-1 py-0.5 rounded">
                    Services/Dicom/DicomAnonymizer.cs
                </code>
                . Keep this list in sync with that file when the allowlist changes.
                Policy reference:{' '}
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
