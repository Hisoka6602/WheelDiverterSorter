using NLog;
using NLog.Extensions.Logging;
using WheelDiverterSorter.Core;
using WheelDiverterSorter.Ingress;
using Microsoft.Extensions.Options;
using WheelDiverterSorter.Execution;
using WheelDiverterSorter.Core.Manager;
using WheelDiverterSorter.Core.Options;
using WheelDiverterSorter.Host.Servers;
using WheelDiverterSorter.Core.Utilities;
using WheelDiverterSorter.Drivers.Vendors.Leadshine;
using WheelDiverterSorter.Drivers.Vendors.ShuDiNiao;

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

            builder.Services.Configure<IReadOnlyList<IoPanelButtonOptions>>(
                builder.Configuration.GetSection("IoPanelButtonOptions"));
            builder.Services.Configure<IReadOnlyList<IoLinkagePointOptions>>(
                builder.Configuration.GetSection("IoLinkagePointOptions"));
            builder.Services.Configure<IReadOnlyList<SensorOptions>>(
                builder.Configuration.GetSection("SensorOptions"));
            builder.Services.Configure<IReadOnlyList<PositionOptions>>(
                builder.Configuration.GetSection("PositionOptions"));
            builder.Services.Configure<IReadOnlyList<ConveyorSegmentOptions>>(
                builder.Configuration.GetSection("ConveyorSegmentOptions"));

            //组件注册
            builder.Services.AddSingleton<ISystemStateManager, SystemStateManager>();
            builder.Services.AddSingleton<SafeExecutor>();
            builder.Services.AddSingleton<IEmcController, LeadshineEmcController>();
            builder.Services.AddSingleton<IIoPanel, LeadshaineIoPanel>();
            builder.Services.AddSingleton<IUpstreamRouting, UpstreamRouting>();
            builder.Services.AddSingleton<IParcelManager, ParcelManager>();

            //服务
            //日志清理服务
            builder.Services.AddHostedService<LogCleanupService>();
            //IO监控服务
            builder.Services.AddHostedService<IoMonitoringHostedService>();
            //IO联动服务
            builder.Services.AddHostedService<IoLinkageHostedService>();
            //摆轮管理服务
            builder.Services.AddOptions<IReadOnlyList<WheelDiverterConnectionOptions>>()
                .Bind(builder.Configuration.GetSection("WheelDiverterConnectionOptions"))
                .Validate(list => list is { Count: > 0 }, "配置无效：WheelDiverterConnectionOptions 不能为空")
                .Validate(list => list.Select(x => x.DiverterId).Distinct().Count() == list.Count, "配置无效：DiverterId 必须唯一")
                .ValidateOnStart();

            builder.Services.AddSingleton<List<IWheelDiverter>>(sp => {
                var options = sp.GetRequiredService<IOptions<IReadOnlyList<WheelDiverterConnectionOptions>>>().Value;

                var ordered = options
                    .OrderBy(static x => x.DiverterId)
                    .ToArray();

                var diverters = new List<IWheelDiverter>(ordered.Length);
                foreach (var opt in ordered) {
                    // 将 opt 作为构造参数传入，用于匹配 ShuDiNiaoWheelDiverter 的构造函数参数
                    var diverter = ActivatorUtilities.CreateInstance<ShuDiNiaoWheelDiverter>(sp, opt);
                    diverters.Add(diverter);
                }

                return diverters;
            });

            builder.Services.AddSingleton<IWheelDiverterManager>(sp => {
                var diverters = sp.GetRequiredService<List<IWheelDiverter>>();
                var logger = sp.GetRequiredService<ILogger<WheelDiverterManager>>();
                return new WheelDiverterManager(diverters, logger);
            });
            builder.Services.AddHostedService<WheelDiverterHostedService>();

            //上游路由连接服务
            builder.Services.AddHostedService<UpstreamRoutingHostedService>();
            //包裹服务
            builder.Services.AddHostedService<UpstreamRoutingHostedService>();
            //站点服务

#if !DEBUG
    builder.Services.AddWindowsService();
#endif
            //
            var host = builder.Build();
            // 添加全局异常处理器以防止崩溃
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => {
                var exception = args.ExceptionObject as Exception;
                logger.Fatal(exception, "未处理的异常发生，应用程序将尝试继续运行");
            };

            TaskScheduler.UnobservedTaskException += (sender, args) => {
                logger.Fatal(args.Exception, "未观察到的任务异常");
                args.SetObserved(); // 防止程序崩溃
            };

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
