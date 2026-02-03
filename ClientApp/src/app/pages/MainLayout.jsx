import { Outlet, useNavigate } from "react-router";
import { Menu, Transition } from "@headlessui/react";
import RadiopaediaLogo from '../../app/Radiopaedia-logo-only-transparent.png';

const MainLayout = ({ children, user, onLogout }) => {
    const navigate = useNavigate();

    const handleMyCases = () => {
        navigate('/my-cases');
    };

    const handleHome = () => {
        navigate('/');
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
                            <Menu.Button className="flex items-center space-x-2 rounded-full hover:bg-slate-100 dark:hover:bg-slate-700 p-2 transition-colors">
                                <div className="h-8 w-8 rounded-full bg-indigo-500 text-white flex items-center justify-center font-bold">
                                    {user.name.charAt(0)}
                                </div>
                                <span className="text-sm font-medium">{user.name}</span>
                                <svg className="w-4 h-4 text-slate-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                                </svg>
                            </Menu.Button>
                            <Transition
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
                                                    className={`${active ? 'bg-indigo-500 text-white' : 'text-gray-900 dark:text-gray-100'
                                                        } group flex w-full items-center rounded-md px-3 py-2 text-sm`}
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
                                                    className={`${active ? 'bg-indigo-500 text-white' : 'text-gray-900 dark:text-gray-100'
                                                        } group flex w-full items-center rounded-md px-3 py-2 text-sm`}
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
                                        <Menu.Item>
                                            {({ active }) => (
                                                <button
                                                    onClick={onLogout}
                                                    className={`${active ? 'bg-red-500 text-white' : 'text-gray-900 dark:text-gray-100'
                                                        } group flex w-full items-center rounded-md px-3 py-2 text-sm`}
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