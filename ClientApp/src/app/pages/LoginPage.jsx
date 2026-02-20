import PropTypes from 'prop-types';
import PurpleImg from '../Purple.jpg';

const LoginPage = ({ hasTopBanner }) => {
    const handleLogin = () => {
        window.location.href = '/api/auth/login';
    };

    return (
        <div className={`flex min-h-screen bg-white dark:bg-slate-900 ${hasTopBanner ? 'pt-12' : ''}`}>
            {/* Left Side - Hero Image */}
            <div className="hidden lg:flex lg:w-1/2 relative bg-indigo-900">
                <img
                    src={PurpleImg}
                    alt="Radiology Splash"
                    className="absolute inset-0 w-full h-full object-cover opacity-80"
                />
                <div className="absolute inset-0 bg-gradient-to-r from-indigo-900/50 to-transparent"></div>
            </div>

            {/* Right Side - Login Form */}
            <div className="flex w-full lg:w-1/2 items-center justify-center p-8 lg:p-16">
                <div className="w-full max-w-md space-y-8">
                    <div className="text-center lg:text-left">
                        <h1 className="text-4xl font-extrabold tracking-tight text-slate-900 dark:text-white sm:text-5xl mb-4">
                            RadiopaediaConnect
                        </h1>
                        <p className="text-lg text-slate-600 dark:text-slate-300">
                            Login using your Radiopaedia account in order to upload images.
                        </p>
                    </div>

                    <div className="mt-8">
                        <button
                            onClick={handleLogin}
                            className="w-full flex justify-center py-3 px-4 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 transition-colors duration-200"
                        >
                            Sign in with Radiopaedia
                        </button>
                        <div className="mt-4 text-center">
                            <span className="text-sm text-slate-500">Need help? <a href="#" className="text-indigo-600 hover:text-indigo-500">Contact Support</a></span>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};

LoginPage.propTypes = {
    hasTopBanner: PropTypes.bool,
};

export default LoginPage;