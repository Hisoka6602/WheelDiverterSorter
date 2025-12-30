using NLog;
using NLog.Extensions.Logging;
using WheelDiverterSorter.Core.Options;

namespace WheelDiverterSorter.Host;
internal class Program {

    private static void Main(string[] args) {
        // 尽早配置NLog
        var logger = LogManager.Setup().LoadConfigurationFromFile("nlog.config").GetCurrentClassLogger();

        try {
            logger.Info("应用程序启动");

            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

            // 配置NLog
            builder.Logging.ClearProviders();
            builder.Logging.AddNLog();

            //配置设置

            builder.Services.Configure<LogCleanupSettings>(
                builder.Configuration.GetSection("LogCleanup"));

            builder.Services.Configure<UpstreamRoutingConnectionOptions>(
                builder.Configuration.GetSection("UpstreamRoutingConnectionOptions"));

            builder.Services.Configure<List<IoPanelButtonOptions>>(
                builder.Configuration.GetSection("IoPanelButtonOptions"));
            builder.Services.Configure<List<IoLinkagePointOptions>>(
                builder.Configuration.GetSection("IoLinkagePointOptions"));
            builder.Services.Configure<List<SensorOptions>>(
                builder.Configuration.GetSection("SensorOptions"));
            builder.Services.Configure<List<WheelDiverterConnectionOptions>>(
                builder.Configuration.GetSection("WheelDiverterConnectionOptions"));
            builder.Services.AddHostedService<Worker>();

            var host = builder.Build();
            host.Run();
        }
        catch (Exception e) {
            logger.Error(e, "应用程序因异常而停止");
        }
        finally {
            LogManager.Shutdown();
        }
    }
}
