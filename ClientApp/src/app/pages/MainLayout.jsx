import { Outlet } from "react-router";
import { Menu, Transition } from "@headlessui/react";
import RadiopaediaLogo from '../../app/Radiopaedia-logo-only-transparent.png';

const MainLayout = ({ children, user, onLogout }) => {
  return (
    <div className="flex min-h-screen flex-col bg-slate-50 dark:bg-slate-900 text-slate-900 dark:text-slate-100 transition-colors duration-300">
      <header className="flex items-center justify-between h-16 px-4 bg-white dark:bg-slate-800 shadow-sm transition-colors duration-300">
        <div className="flex items-center">
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
              </Menu.Button>
              <Transition
                enter="transition duration-100 ease-out"
                enterFrom="transform scale-95 opacity-0"
                enterTo="transform scale-100 opacity-100"
                leave="transition duration-75 ease-out"
                leaveFrom="transform scale-100 opacity-100"
                leaveTo="transform scale-95 opacity-0"
              >
                <Menu.Items className="absolute right-0 mt-2 w-48 origin-top-right divide-y divide-gray-100 rounded-md bg-white shadow-lg ring-1 ring-black ring-opacity-5 focus:outline-none dark:bg-slate-800 dark:divide-slate-700">
                  <div className="p-1">
                    <Menu.Item>
                      {({ active }) => (
                        <button
                          onClick={onLogout}
                          className={`${
                            active ? 'bg-indigo-500 text-white' : 'text-gray-900 dark:text-gray-100'
                          } group flex w-full items-center rounded-md px-2 py-2 text-sm`}
                        >
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