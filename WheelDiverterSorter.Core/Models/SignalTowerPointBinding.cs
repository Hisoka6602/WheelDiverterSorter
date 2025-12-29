using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using WheelDiverterSorter.Core.Enums;

namespace WheelDiverterSorter.Core.Models {
    /// <summary>
    /// 信号塔点位绑定信息
    /// </summary>
    public sealed record class SignalTowerPointBinding {
        /// <summary>
        /// 红灯输出点位
        /// </summary>
        public required int RedPoint { get; init; }

        /// <summary>
        /// 红灯输出有效电平
        /// </summary>
        public required IoState RedActiveState { get; init; }

        /// <summary>
        /// 黄灯输出点位
        /// </summary>
        public required int YellowPoint { get; init; }

        /// <summary>
        /// 黄灯输出有效电平
        /// </summary>
        public required IoState YellowActiveState { get; init; }

        /// <summary>
        /// 绿灯输出点位
        /// </summary>
        public required int GreenPoint { get; init; }

        /// <summary>
        /// 绿灯输出有效电平
        /// </summary>
        public required IoState GreenActiveState { get; init; }

        /// <summary>
        /// 蜂鸣器输出点位
        /// </summary>
        public required int BuzzerPoint { get; init; }

        /// <summary>
        /// 蜂鸣器输出有效电平
        /// </summary>
        public required IoState BuzzerActiveState { get; init; }
    }
}
