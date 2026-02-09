import { Outlet, useNavigate } from "react-router";
import { Menu, Transition } from "@headlessui/react";
import { Fragment, useState, useCallback } from "react";
import RadiopaediaLogo from '../../app/Radiopaedia-logo-only-transparent.png';

const MainLayout = ({ children, user, onLogout }) => {
    const navigate = useNavigate();
    const [quota, setQuota] = useState(null);
    const [quotaLoading, setQuotaLoading] = useState(false);

    const fetchQuota = useCallback(async () => {
        if (!user) return;
        setQuotaLoading(true);
        try {
            const res = await fetch('/api/auth/quota');
            if (res.ok) {
                const data = await res.json();
                setQuota(data);
            }
        } catch (err) {
            console.error("Failed to fetch quota", err);
        } finally {
            setQuotaLoading(false);
        }
    }, [user]);

    const handleMenuOpen = () => {
        fetchQuota();
    };

    const handleMyCases = () => {
        navigate('/my-cases');
    };

    const handleHome = () => {
        navigate('/');
    };

    const getRadiopaediaUrl = (filter = null) => {
        const baseUrl = `https://radiopaedia.org/users/${user.name}/cases`;
        return filter ? `${baseUrl}?visibility=${filter}` : baseUrl;
    };

    return (
        <div className="flex min-h-screen flex-col bg-slate-50 dark:bg-slate-900 text-slate-900 dark:text-slate-100 transition-colors duration-300">
            <header className="flex items-center justify-between h-16 px-4 bg-white dark:bg-slate-800 shadow-sm transition-colors duration-300">
                <div className="flex items-center cursor-pointer" onClick={handleHome}>
                    <img src={RadiopaediaLogo} alt="Radiopaedia Logo" className="h-8 w-auto mr-3" />
                    <h1 className="text-xl font-bold">RadiopaediaConnect</h1>
                </div>
                {user && (
                    <div className="relative">
                        <Menu>
                            <Menu.Button
                                onClick={handleMenuOpen}
                                className="flex items-center space-x-2 rounded-full hover:bg-slate-100 dark:hover:bg-slate-700 p-2 transition-colors"
                            >
                                <div className="h-8 w-8 rounded-full bg-indigo-500 text-white flex items-center justify-center font-bold">
                                    {user.name.charAt(0)}
                                </div>
                                <span className="text-sm font-medium">{user.name}</span>
                                <svg className="w-4 h-4 text-slate-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                                </svg>
                            </Menu.Button>
                            <Transition
                                as={Fragment}
                                enter="transition duration-100 ease-out"
                                enterFrom="transform scale-95 opacity-0"
                                enterTo="transform scale-100 opacity-100"
                                leave="transition duration-75 ease-out"
                                leaveFrom="transform scale-100 opacity-100"
                                leaveTo="transform scale-95 opacity-0"
                            >
                                <Menu.Items className="absolute right-0 mt-2 w-56 origin-top-right divide-y divide-gray-100 rounded-md bg-white shadow-lg ring-1 ring-black ring-opacity-5 focus:outline-none dark:bg-slate-800 dark:divide-slate-700 z-50">
                                    <div className="p-1">
                                        <Menu.Item>
                                            {({ active }) => (
                                                <button
                                                    onClick={handleHome}
                                                    className={`${active ? 'bg-indigo-500 text-white' : 'text-gray-900 dark:text-gray-100'} group flex w-full items-center rounded-md px-3 py-2 text-sm`}
                                                >
                                                    <svg className="w-4 h-4 mr-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6" />
                                                    </svg>
                                                    Home
                                                </button>
                                            )}
                                        </Menu.Item>
                                        <Menu.Item>
                                            {({ active }) => (
                                                <button
                                                    onClick={handleMyCases}
                                                    className={`${active ? 'bg-indigo-500 text-white' : 'text-gray-900 dark:text-gray-100'} group flex w-full items-center rounded-md px-3 py-2 text-sm`}
                                                >
                                                    <svg className="w-4 h-4 mr-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                                                    </svg>
                                                    My Cases
                                                </button>
                                            )}
                                        </Menu.Item>
                                    </div>
                                    <div className="p-1">
                                        <div className="px-3 py-2 text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                                            Radiopaedia
                                        </div>
                                        <div className="px-3 py-2 text-xs text-slate-600 dark:text-slate-400 flex items-center">
                                            <svg className="w-4 h-4 mr-2 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
                                            </svg>
                                            {quotaLoading ? (
                                                <span className="text-slate-400">Loading...</span>
                                            ) : quota ? (
                                                    <span>Draft Case Quota: <span className="font-medium text-slate-700 dark:text-slate-300">
                                                        {quota.current === 0 ? '\u221E' : quota.current}/{quota.maximum === 0 ? '\u221E' : quota.maximum}
                                                    </span></span>
                                            ) : (
                                                <span className="text-slate-400">{'\u2014'}</span>
                                            )}
                                        </div>
                                        <Menu.Item>
                                            {({ active }) => (
                                                <a
                                                    href={getRadiopaediaUrl()}
                                                    target="_blank"
                                                    rel="noopener noreferrer"
                                                    className={`${active ? 'bg-indigo-500 text-white' : 'text-gray-900 dark:text-gray-100'} group flex w-full items-center rounded-md px-3 py-2 text-sm`}
                                                >
                                                    <svg className="w-4 h-4 mr-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                                                    </svg>
                                                    All Cases
                                                </a>
                                            )}
                                        </Menu.Item>
                                        <Menu.Item>
                                            {({ active }) => (
                                                <a
                                                    href={getRadiopaediaUrl('draft')}
                                                    target="_blank"
                                                    rel="noopener noreferrer"
                                                    className={`${active ? 'bg-indigo-500 text-white' : 'text-gray-900 dark:text-gray-100'} group flex w-full items-center rounded-md px-3 py-2 text-sm`}
                                                >
                                                    <svg className="w-4 h-4 mr-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                                                    </svg>
                                                    Draft Cases
                                                </a>
                                            )}
                                        </Menu.Item>
                                        <Menu.Item>
                                            {({ active }) => (
                                                <a
                                                    href={getRadiopaediaUrl('public')}
                                                    target="_blank"
                                                    rel="noopener noreferrer"
                                                    className={`${active ? 'bg-indigo-500 text-white' : 'text-gray-900 dark:text-gray-100'} group flex w-full items-center rounded-md px-3 py-2 text-sm`}
                                                >
                                                    <svg className="w-4 h-4 mr-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                                                    </svg>
                                                    Public Cases
                                                </a>
                                            )}
                                        </Menu.Item>
                                        <Menu.Item>
                                            {({ active }) => (
                                                <a
                                                    href={getRadiopaediaUrl('unlisted')}
                                                    target="_blank"
                                                    rel="noopener noreferrer"
                                                    className={`${active ? 'bg-indigo-500 text-white' : 'text-gray-900 dark:text-gray-100'} group flex w-full items-center rounded-md px-3 py-2 text-sm`}
                                                >
                                                    <svg className="w-4 h-4 mr-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 5.411m0 0L21 21" />
                                                    </svg>
                                                    Unlisted Cases
                                                </a>
                                            )}
                                        </Menu.Item>
                                    </div>
                                    <div className="p-1">
                                        <Menu.Item>
                                            {({ active }) => (
                                                <button
                                                    onClick={onLogout}
                                                    className={`${active ? 'bg-red-500 text-white' : 'text-gray-900 dark:text-gray-100'} group flex w-full items-center rounded-md px-3 py-2 text-sm`}
                                                >
                                                    <svg className="w-4 h-4 mr-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
                                                    </svg>
                                                    Log out
                                                </button>
                                            )}
                                        </Menu.Item>
                                    </div>
                                </Menu.Items>
                            </Transition>
                        </Menu>
                    </div>
                )}
            </header>
            <main className="flex-grow p-4">
                <div className="container mx-auto">
                    {children || <Outlet />}
                </div>
            </main>
        </div>
    );
};

export default MainLayout;