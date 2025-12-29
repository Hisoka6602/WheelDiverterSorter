namespace WheelDiverterSorter.Drivers.Vendors.ShuDiNiao;

/// <summary>
/// 数递鸟协议工具类
/// </summary>
/// <remarks>
/// 负责数递鸟TCP协议的打包和解析：
///
/// **标准帧（7字节）**：
/// - 起始字节：0x51 0x52
/// - 长度字节：0x57（固定）
/// - 设备地址：0x51/0x52/...
/// - 消息类型：0x51/0x52/0x53
/// - 状态/命令码：根据消息类型不同
/// - 结束字符：0xFE
///
/// **速度设置帧（12字节）**：
/// - 起始字节：0x51 0x52
/// - 长度字节：0x5C（12字节长度标记）
/// - 设备地址：0x51/0x52/...
/// - 消息类型：0x54（速度设置）
/// - 速度数据：6字节（摆动速度+摆动后速度+4字节备用）
/// - 结束字符：0xFE
/// </remarks>
internal static class ShuDiNiaoProtocol {

    // 协议常量
    private const byte StartByte1 = 0x51;

    private const byte StartByte2 = 0x52;
    private const byte StandardLengthByte = 0x57;
    private const byte SpeedSettingLengthByte = 0x5C;
    private const byte EndByte = 0xFE;

    /// <summary>
    /// 速度设置帧备用字节填充值（数递鸟协议规范要求备用字节必须为 0x5A）
    /// </summary>
    private const byte ReservedByteFillValue = 0x5A;

    /// <summary>
    /// 标准协议帧长度（字节）
    /// </summary>
    internal const int FrameLength = 7;

    /// <summary>
    /// 速度设置帧长度（字节）
    /// </summary>
    internal const int SpeedSettingFrameLength = 12;

    // 字节位置索引
    private const int StartByte1Index = 0;

    private const int StartByte2Index = 1;
    private const int LengthByteIndex = 2;
    private const int DeviceAddressIndex = 3;
    private const int MessageTypeIndex = 4;
    private const int DataByteIndex = 5;
    private const int EndByteIndex = 6;

    /// <summary>
    /// 构造控制命令帧（信息二）
    /// </summary>
    /// <param name="deviceAddress">设备地址（0x51, 0x52, ...）</param>
    /// <param name="command">控制命令码</param>
    /// <returns>7字节命令帧</returns>
    public static byte[] BuildCommandFrame(byte deviceAddress, ShuDiNiaoControlCommand command) {
        return new byte[]
        {
            StartByte1,                                 // [0] 起始字节1
            StartByte2,                                 // [1] 起始字节2
            StandardLengthByte,                         // [2] 长度字节
            deviceAddress,                              // [3] 设备地址
            (byte)ShuDiNiaoMessageType.ControlCommand, // [4] 消息类型：控制命令
            (byte)command,                              // [5] 命令码
            EndByte                                     // [6] 结束字符
        };
    }

    /// <summary>
    /// 构造速度设置帧（信息四）
    /// </summary>
    /// <param name="deviceAddress">设备地址（0x51, 0x52, ...）</param>
    /// <param name="speed">摆动速度（m/min），范围 0-255</param>
    /// <param name="speedAfterSwing">摆动后速度（m/min），范围 0-255</param>
    /// <returns>12字节速度设置帧</returns>
    /// <remarks>
    /// 示例报文：51 52 5C XX 54 5A 5A 5A 5A 5A 5A FE
    /// 详解：
    ///   51 -> 起始字节
    ///   52 -> 起始字节
    ///   5C -> 整串长度（12字节）
    ///   XX -> 设备号（根据实际配置，如 0x51/0x52/...）
    ///   54 -> 消息类型（速度设置）
    ///   5A -> 速度设置（示例：90m/min 对应 0x5A）
    ///   5A -> 摆动后速度（示例：90m/min 对应 0x5A）
    ///   5A -> 备用
    ///   5A -> 备用
    ///   5A -> 备用
    ///   5A -> 备用
    ///   FE -> 结束字符
    /// </remarks>
    public static byte[] BuildSpeedSettingFrame(byte deviceAddress, byte speed, byte speedAfterSwing) {
        return new byte[]
        {
            StartByte1,                                  // [0] 起始字节1
            StartByte2,                                  // [1] 起始字节2
            SpeedSettingLengthByte,                      // [2] 长度字节（0x5C=12字节）
            deviceAddress,                               // [3] 设备地址
            (byte)ShuDiNiaoMessageType.SpeedSetting,    // [4] 消息类型：速度设置
            speed,                                       // [5] 摆动速度（m/min）
            speedAfterSwing,                             // [6] 摆动后速度（m/min）
            ReservedByteFillValue,                       // [7] 备用（协议规定必须为0x5A）
            ReservedByteFillValue,                       // [8] 备用（协议规定必须为0x5A）
            ReservedByteFillValue,                       // [9] 备用（协议规定必须为0x5A）
            ReservedByteFillValue,                       // [10] 备用（协议规定必须为0x5A）
            EndByte                                      // [11] 结束字符
        };
    }

    /// <summary>
    /// 尝试解析设备状态上报帧（信息一）
    /// </summary>
    /// <param name="frame">接收到的字节数据</param>
    /// <param name="deviceAddress">解析出的设备地址</param>
    /// <param name="deviceState">解析出的设备状态</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParseDeviceStatus(
        ReadOnlySpan<byte> frame,
        out byte deviceAddress,
        out ShuDiNiaoDeviceState deviceState) {
        deviceAddress = 0;
        deviceState = default;

        // 校验帧长度
        if (frame.Length != FrameLength) {
            return false;
        }

        // 校验固定字节
        if (!ValidateFixedBytes(frame)) {
            return false;
        }

        // 校验消息类型
        if (frame[MessageTypeIndex] != (byte)ShuDiNiaoMessageType.DeviceStatus) {
            return false;
        }

        // 校验状态码
        byte stateByte = frame[DataByteIndex];
        if (!Enum.IsDefined(typeof(ShuDiNiaoDeviceState), stateByte)) {
            return false;
        }

        // 解析成功
        deviceAddress = frame[DeviceAddressIndex];
        deviceState = (ShuDiNiaoDeviceState)stateByte;
        return true;
    }

    /// <summary>
    /// 尝试解析应答与完成帧（信息三）
    /// </summary>
    /// <param name="frame">接收到的字节数据</param>
    /// <param name="deviceAddress">解析出的设备地址</param>
    /// <param name="responseCode">解析出的应答/完成码</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParseResponse(
        ReadOnlySpan<byte> frame,
        out byte deviceAddress,
        out ShuDiNiaoResponseCode responseCode) {
        deviceAddress = 0;
        responseCode = default;

        // 校验帧长度
        if (frame.Length != FrameLength) {
            return false;
        }

        // 校验固定字节
        if (!ValidateFixedBytes(frame)) {
            return false;
        }

        // 校验消息类型
        if (frame[MessageTypeIndex] != (byte)ShuDiNiaoMessageType.ResponseAndCompletion) {
            return false;
        }

        // 校验应答码
        byte responseByte = frame[DataByteIndex];
        if (!Enum.IsDefined(typeof(ShuDiNiaoResponseCode), responseByte)) {
            return false;
        }

        // 解析成功
        deviceAddress = frame[DeviceAddressIndex];
        responseCode = (ShuDiNiaoResponseCode)responseByte;
        return true;
    }

    /// <summary>
    /// 校验固定字节（起始字节、长度字节、结束字节）
    /// </summary>
    private static bool ValidateFixedBytes(ReadOnlySpan<byte> frame) {
        if (frame.Length == FrameLength) {
            // 标准7字节帧
            return frame[StartByte1Index] == StartByte1 &&
                   frame[StartByte2Index] == StartByte2 &&
                   frame[LengthByteIndex] == StandardLengthByte &&
                   frame[EndByteIndex] == EndByte;
        }
        else if (frame.Length == SpeedSettingFrameLength) {
            // 速度设置12字节帧
            return frame[StartByte1Index] == StartByte1 &&
                   frame[StartByte2Index] == StartByte2 &&
                   frame[LengthByteIndex] == SpeedSettingLengthByte &&
                   frame[11] == EndByte;
        }

        return false;
    }

    /// <summary>
    /// 格式化字节数组为十六进制字符串（用于日志）
    /// </summary>
    public static string FormatBytes(ReadOnlySpan<byte> bytes) {
        return string.Join(" ", bytes.ToArray().Select(b => $"{b:X2}"));
    }
}
