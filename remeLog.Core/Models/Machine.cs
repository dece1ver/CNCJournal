using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models
{
    public class Machine
    {
        #region Основные

        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsSerial { get; set; }
        public string Type { get; set; } = string.Empty;
        public int SetupLimit { get; set; }
        public double SetupCoefficient { get; set; }

        #endregion

        #region Winnum

        public int WnId { get; set; }
        public Guid WnUuid { get; set; }

        #endregion

        #region Сигналы - общие

        public string WnCounterSignal { get; set; } = string.Empty;
        public string WnNcProgramNameSignal { get; set; } = string.Empty;
        public string WnNcPartNameSignal { get; set; } = string.Empty;
        public string WnCurrentCSSignal { get; set; } = string.Empty;
        public string WnCurrentPlaneSignal { get; set; } = string.Empty;
        public string WnCurrentBlockNumberSignal { get; set; } = string.Empty;
        public string WnCurrentBlockTextSignal { get; set; } = string.Empty;
        public string WnNcModeSignal { get; set; } = string.Empty;
        public string WnFeedHoldSignal { get; set; } = string.Empty;
        public string WnSBKSignal { get; set; } = string.Empty;
        public string WnDryRunSignal { get; set; } = string.Empty;
        public string WnMSTLKSignal { get; set; } = string.Empty;
        public string WnMLKSignal { get; set; } = string.Empty;
        public string WnProgramRunningSignal { get; set; } = string.Empty;
        public string WnStopSignal { get; set; } = string.Empty;
        public string WnOpStopSignal { get; set; } = string.Empty;
        public string WnMcodeSignal { get; set; } = string.Empty;
        public string WnGMoveSignal { get; set; } = string.Empty;
        public string WnGCycleSignal { get; set; } = string.Empty;
        public string WnGcodeSignal { get; set; } = string.Empty;

        #endregion

        #region Сигналы - коррекции

        public string WnRapidMultiplierSignal { get; set; } = string.Empty;
        public string WnFeedMultiplierSignal { get; set; } = string.Empty;
        public string WnSpindleSpeed1MultiplierSignal { get; set; } = string.Empty;
        public string WnSpindleSpeed2MultiplierSignal { get; set; } = string.Empty;

        #endregion

        #region Сигналы - актуальные значения

        public string WnActSpindle1SpeedSignal { get; set; } = string.Empty;
        public string WnActSpindle2SpeedSignal { get; set; } = string.Empty;
        public string WnActCutSpeedSignal { get; set; } = string.Empty;
        public string WnActFeedPerMinSignal { get; set; } = string.Empty;
        public string WnActFeedPerRevSignal { get; set; } = string.Empty;

        #endregion

        #region Сигналы - абсолютные координаты

        public string WnAbsXSignal { get; set; } = string.Empty;
        public string WnAbsYSignal { get; set; } = string.Empty;
        public string WnAbsZSignal { get; set; } = string.Empty;
        public string WnAbsZASignal { get; set; } = string.Empty;
        public string WnAbsASignal { get; set; } = string.Empty;
        public string WnAbsBSignal { get; set; } = string.Empty;
        public string WnAbsCSignal { get; set; } = string.Empty;
        public string WnAbsWSignal { get; set; } = string.Empty;

        #endregion

        #region Сигналы - относительные координаты

        public string WnRelXSignal { get; set; } = string.Empty;
        public string WnRelYSignal { get; set; } = string.Empty;
        public string WnRelZSignal { get; set; } = string.Empty;
        public string WnRelZASignal { get; set; } = string.Empty;
        public string WnRelASignal { get; set; } = string.Empty;
        public string WnRelBSignal { get; set; } = string.Empty;
        public string WnRelCSignal { get; set; } = string.Empty;
        public string WnRelWSignal { get; set; } = string.Empty;

        #endregion

        #region Сигналы - машинные координаты

        public string WnMachXSignal { get; set; } = string.Empty;
        public string WnMachYSignal { get; set; } = string.Empty;
        public string WnMachZSignal { get; set; } = string.Empty;
        public string WnMachZASignal { get; set; } = string.Empty;
        public string WnMachASignal { get; set; } = string.Empty;
        public string WnMachBSignal { get; set; } = string.Empty;
        public string WnMachCSignal { get; set; } = string.Empty;
        public string WnMachWSignal { get; set; } = string.Empty;

        #endregion

        #region Сигналы - инструмент

        public string WnToolSignal { get; set; } = string.Empty;
        public string WnToolHSignal { get; set; } = string.Empty;
        public string WnToolDSignal { get; set; } = string.Empty;
        public string WnToolVectorSignal { get; set; } = string.Empty;
        public string WnToolGeomRSignal { get; set; } = string.Empty;
        public string WnToolGeomXSignal { get; set; } = string.Empty;
        public string WnToolGeomYSignal { get; set; } = string.Empty;
        public string WnToolGeomZSignal { get; set; } = string.Empty;
        public string WnToolWearRSignal { get; set; } = string.Empty;
        public string WnToolWearXSignal { get; set; } = string.Empty;
        public string WnToolWearYSignal { get; set; } = string.Empty;
        public string WnToolWearZSignal { get; set; } = string.Empty;

        #endregion

        #region Сигналы - аварии и нагрузки

        public string WnAlarmMessageSignal { get; set; } = string.Empty;
        public string WnLoadXSignal { get; set; } = string.Empty;
        public string WnLoadYSignal { get; set; } = string.Empty;
        public string WnLoadZSignal { get; set; } = string.Empty;
        public string WnLoadCSignal { get; set; } = string.Empty;
        public string WnLoadBSignal { get; set; } = string.Empty;
        public string WnLoadWSignal { get; set; } = string.Empty;
        public string WnLoadSpindle1Signal { get; set; } = string.Empty;
        public string WnLoadSpindle2Signal { get; set; } = string.Empty;

        #endregion
    }
}
