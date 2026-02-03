import { createBrowserRouter, Outlet } from "react-router";
import HomePage from "app/index";
import MainLayout from "app/pages/MainLayout";

const router = createBrowserRouter([
    {
        path: "/",
        element: <Outlet />,
        children: [
            {
                index: true,
                element: <HomePage />,
            },
            {
                path: "my-cases",
                lazy: async () => ({
                    Component: (await import("app/pages/MyCasesPage")).default,
                }),
            },
            {
                path: "debug/cornerstone",
                element: (
                    <MainLayout user={{ name: "Debug User" }}>
                        <Outlet />
                    </MainLayout>
                ),
                children: [
                    {
                        path: "",
                        lazy: async () => ({
                            Component: (await import("app/pages/debug/cornerstone/CornerstoneTestPage")).default,
                        }),
                    }
                ]
            },
        ],
    },
]);

export default router;