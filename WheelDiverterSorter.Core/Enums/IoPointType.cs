using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WheelDiverterSorter.Core.Enums {

    /// <summary>
    /// IO 点位类型枚举
    /// </summary>
    public enum IoPointType {

        /// <summary>
        /// IO 面板按钮
        /// </summary>
        [Description("IO面板按钮")]
        PanelButton = 0,

        /// <summary>
        /// 创建包裹传感器
        /// </summary>
        [Description("创建包裹传感器")]
        ParcelCreateSensor = 1,

        /// <summary>
        /// 摆轮位置传感器
        /// </summary>
        [Description("摆轮位置传感器")]
        DiverterPositionSensor = 2,

        /// <summary>
        /// 落格传感器
        /// </summary>
        [Description("落格传感器")]
        ChuteDropSensor = 3,

        /// <summary>
        /// 阻塞检测传感器
        /// </summary>
        [Description("阻塞检测传感器")]
        BlockageDetectionSensor = 4
    }
}
