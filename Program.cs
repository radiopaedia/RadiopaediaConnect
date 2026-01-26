using FellowOakDicom;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Extensions;
using RadiopaediaConnect.Models;
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

            var dbName = builder.Configuration["DatabaseName"] ?? "RadiopaediaConnect.db";
            var connectionString = $"Data Source={Path.Combine(dataFolder, dbName)};Cache=Shared";

            builder.Configuration.AddJsonFile(Path.Combine(dataFolder, "appsettings.json"), optional: true, reloadOnChange: true);
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

            builder.Services.Configure<DicomSettings>(builder.Configuration.GetSection(DicomSettings.SectionName));
            builder.Services.AddSingleton<UserRepository>();
            builder.Services.AddSingleton<DicomRepository>(sp => new DicomRepository(connectionString));
            builder.Services.AddHttpClient<IOAuthService, OAuthService>();

            builder.Services.AddRadiopaediaAuthentication(builder.Configuration);

            builder.Services.AddTransient<DicomScu>();
            builder.Services.AddScoped<CaseProcessorService>();
            builder.Services.AddHostedService<DicomQueueWorker>();
            builder.Services.AddHttpClient<RadiopaediaApiClient>();

            var app = builder.Build();

            var dicomScp = new DicomScp(app.Configuration, app.Services.GetRequiredService<DicomRepository>());
            try { dicomScp.Start(); }
            catch (Exception ex) { Console.WriteLine($"[CRITICAL] Could not start DICOM Server: {ex.Message}"); return; }
            app.Lifetime.ApplicationStopping.Register(() => dicomScp.Stop());

            app.UseForwardedHeaders();

            app.UseCookiePolicy(new CookiePolicyOptions
            {
                MinimumSameSitePolicy = SameSiteMode.Lax,
                HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.None,
                Secure = CookieSecurePolicy.SameAsRequest
            });

            RadiopaediaConnect.Data.DbInitializer.Initialize(connectionString);
            RadiopaediaConnect.Data.DicomDbInitializer.Initialize(connectionString);

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