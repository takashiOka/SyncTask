using Microsoft.Extensions.Logging;
using SyncTask.Services;

namespace SyncTask
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddSingleton<DailyLogService>();
            builder.Services.AddSingleton<HttpClient>();
            builder.Services.AddSingleton<RedmineService>();
            builder.Services.AddSingleton<AttendanceExcelService>();
            builder.Services.AddSingleton<IHolidayService, JapaneseHolidayService>();
            builder.Services.AddSingleton<IFileSaveService, FileSaveService>();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
