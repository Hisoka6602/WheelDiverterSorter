namespace WheelDiverterSorter.Drivers.Vendors.ShuDiNiao;

/// <summary>
/// 数递鸟摆轮速度单位转换工具
/// </summary>
/// <remarks>
/// API 端点使用 mm/s 作为输入单位，数递鸟摆轮设备使用 m/min 作为协议单位。
/// 此类负责两者之间的转换。
/// </remarks>
internal static class ShuDiNiaoSpeedConverter {

    /// <summary>
    /// 将速度从 mm/s 转换为 m/min
    /// </summary>
    /// <param name="speedMmPerSecond">速度（毫米/秒）</param>
    /// <returns>速度（米/分钟），限制在 0-255 范围内</returns>
    /// <remarks>
    /// 转换公式：m/min = (mm/s) / 1000 * 60 = (mm/s) * 0.06
    ///
    /// 示例：
    /// - 1500 mm/s = 90 m/min
    /// - 1000 mm/s = 60 m/min
    /// - 500 mm/s = 30 m/min
    /// </remarks>
    public static byte ConvertMmPerSecondToMPerMin(double speedMmPerSecond) {
        if (speedMmPerSecond < 0) {
            return 0;
        }

        // mm/s * 0.06 = m/min
        double speedMPerMin = speedMmPerSecond * 0.06;

        // 限制在设备支持的范围内（0-255 m/min）
        if (speedMPerMin > 255) {
            return 255;
        }

        return (byte)Math.Round(speedMPerMin);
    }

    /// <summary>
    /// 将速度从 m/min 转换为 mm/s
    /// </summary>
    /// <param name="speedMPerMin">速度（米/分钟）</param>
    /// <returns>速度（毫米/秒）</returns>
    /// <remarks>
    /// 转换公式：mm/s = (m/min) / 0.06 = (m/min) * 1000 / 60 ≈ (m/min) * 16.6667
    ///
    /// 示例：
    /// - 90 m/min = 1500 mm/s
    /// - 60 m/min = 1000 mm/s
    /// - 30 m/min = 500 mm/s
    /// </remarks>
    public static double ConvertMPerMinToMmPerSecond(byte speedMPerMin) {
        // m/min / 0.06 = mm/s
        return speedMPerMin / 0.06;
    }

    /// <summary>
    /// 验证速度值是否在有效范围内
    /// </summary>
    /// <param name="speedMmPerSecond">速度（毫米/秒）</param>
    /// <param name="minSpeed">最小速度（毫米/秒），默认0</param>
    /// <param name="maxSpeed">最大速度（毫米/秒），默认4250（对应255 m/min）</param>
    /// <returns>是否在有效范围内</returns>
    public static bool IsValidSpeed(double speedMmPerSecond, double minSpeed = 0, double maxSpeed = 4250) {
        return speedMmPerSecond >= minSpeed && speedMmPerSecond <= maxSpeed;
    }
}
