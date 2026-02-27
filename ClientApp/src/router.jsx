import { createBrowserRouter, Outlet } from 'react-router';
import HomePage from 'app/index';

const router = createBrowserRouter([
    {
        path: '/',
        element: <Outlet />,
        children: [
            {
                index: true,
                element: <HomePage />,
            },
            {
                path: 'my-cases',
                lazy: async () => ({
                    Component: (await import('app/pages/MyCasesPage')).default,
                }),
            },
            {
                path: 'setup',
                lazy: async () => ({
                    Component: (await import('app/pages/SetupPage')).default,
                }),
            },
            {
                path: 'settings',
                lazy: async () => ({
                    Component: (await import('app/pages/SettingsPage')).default,
                }),
            },
        ],
    },
]);

export default router;