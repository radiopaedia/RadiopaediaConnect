using FellowOakDicom;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Extensions;
using RadiopaediaConnect.Logging;
using RadiopaediaConnect.Services;
using RadiopaediaConnect.Services.Dicom;
using System.Runtime.InteropServices;
using FellowOakDicom.Imaging;

namespace RadiopaediaConnect
{
    public class Program
    {
        public static void Main(string[] args)
        {
            new DicomSetupBuilder()
                .RegisterServices(s => s
                .AddFellowOakDicom()
                .AddImageManager<ImageSharpImageManager>()
                ).Build();

            var builder = WebApplication.CreateBuilder(args);

            string dataFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\data" : "/data";
            var envDataPath = Environment.GetEnvironmentVariable("RCONNECT_DATA_PATH");
            if (!string.IsNullOrEmpty(envDataPath)) dataFolder = envDataPath;
            if (!Directory.Exists(dataFolder)) Directory.CreateDirectory(dataFolder);

            builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataFolder, "keys")))
                .SetApplicationName("RadiopaediaConnect");

            // Database name is hardcoded; no need to make it configurable
            var dbName = "RadiopaediaConnect.db";
            var connectionString = $"Data Source={Path.Combine(dataFolder, dbName)};Cache=Shared";

            builder.Configuration["ConnectionStrings:DefaultConnection"] = connectionString;

            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.All;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
                options.ForwardLimit = 2;
            });

            builder.Services.AddControllersWithViews();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddHttpContextAccessor();

            builder.Services.AddMemoryCache();

            // Register repositories
            builder.Services.AddSingleton<UserRepository>();
            builder.Services.AddSingleton<DicomRepository>(sp => new DicomRepository(connectionString));
            builder.Services.AddSingleton<SettingsRepository>(sp => new SettingsRepository(connectionString));
            builder.Services.AddSingleton<AppLogsRepository>(sp => new AppLogsRepository(connectionString));

            // Register SettingsService (singleton with caching)
            builder.Services.AddSingleton<SettingsService>();

            // Register notification service
            builder.Services.AddSingleton<INotificationService, SmtpNotificationService>();
            builder.Services.AddSingleton<AdminSessionService>();

            // Register DicomScpManager (replaces old DicomScp)
            builder.Services.AddSingleton<DicomScpManager>();

            builder.Services.AddHttpClient<IOAuthService, OAuthService>();

            // OAuth now reads credentials from DB via PostConfigure
            builder.Services.AddRadiopaediaAuthentication();

            builder.Services.AddTransient<DicomScu>();
            builder.Services.AddTransient<RadiopaediaConnect.Services.Dicom.DicomAnonymizer>();
            builder.Services.AddScoped<CaseProcessorService>();
            builder.Services.AddHostedService<DicomQueueWorker>();
            builder.Services.AddHttpClient<RadiopaediaApiClient>();

            var app = builder.Build();

            // Initialize databases
            DbInitializer.Initialize(connectionString);
            DicomDbInitializer.Initialize(connectionString);
            SettingsDbInitializer.Initialize(connectionString);

            // Wire persistent database logger
            var logRepo = app.Services.GetRequiredService<AppLogsRepository>();
            var dbLogProvider = new DatabaseLoggerProvider(logRepo);
            app.Services.GetRequiredService<ILoggerFactory>().AddProvider(dbLogProvider);

            // Start the DICOM SCP
            var scpManager = app.Services.GetRequiredService<DicomScpManager>();
            try
            {
                // Load AE Title from settings DB (synchronous at startup)
                var settingsRepo = app.Services.GetRequiredService<SettingsRepository>();
                var localSettings = settingsRepo.GetLocalSettingsAsync().GetAwaiter().GetResult();
                var aeTitle = localSettings.StorageScpAeTitle ?? "RCONNECT_SCP";
                var remoteNodes = settingsRepo.GetRemoteNodesAsync().GetAwaiter().GetResult();
                var allowedAeTitles = remoteNodes.Select(n => n.AeTitle).Where(ae => !string.IsNullOrWhiteSpace(ae));

                scpManager.Start(aeTitle, allowedAeTitles);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] Could not start DICOM Server: {ex.Message}");
                return;
            }
            app.Lifetime.ApplicationStopping.Register(() => scpManager.Stop());

            app.UseForwardedHeaders();

            app.UseCookiePolicy(new CookiePolicyOptions
            {
                MinimumSameSitePolicy = SameSiteMode.Lax,
                HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.None,
                Secure = CookieSecurePolicy.SameAsRequest
            });

            if (!app.Environment.IsDevelopment()) app.UseHsts();
            else
            {
                app.UseCors(policy => policy
                    .WithOrigins("https://localhost:5173", "http://172.28.43.69:5173", "https://andydev3.ssg.org.au:7191", "https://andydev3.ssg.org.au")
                    .AllowAnyMethod().AllowAnyHeader().AllowCredentials());
            }

            app.UseStaticFiles();
            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();
            app.MapFallbackToFile("index.html");

            app.Run();
        }
    }
}