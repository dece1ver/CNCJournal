using libeLog.Extensions;
using libeLog.Views;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace libeLog.FanucApi
{
    public class FanucService
    {
        public int GetMaxRpm(ushort handle)
        {
            var odspn = new Focas1.ODBSPN();
            var rpm = Focas1.cnc_rdspmaxrpm(handle, Focas1.ALL_SPINDLES, odspn) == Focas1.EW_OK ? odspn.data[0] : 0;
            return rpm == 0 ? 5000 : rpm;
        }

        public (int speed, int feed) GetActRpmAndFeedrate(ushort handle)
        {
            var odbspeed = new Focas1.ODBSPEED();
            var _ret = Focas1.cnc_rdspeed(handle, -1, odbspeed);
            if (_ret != Focas1.EW_OK) MessageBoxWindow.Show(_ret.ToString(), "cnc_rdspeed", MessageBoxButton.OK, MessageBoxImage.Error);
            return (odbspeed.acts.data, odbspeed.actf.data);
        }

        public bool GetOpSignal(ushort handle)
        {
            return handle != 0 && GetPmcSignal(handle, 1, 0, 0, 0, 9).cdata[0].GetBit(7);
        }

        private Focas1.IODBPMC0 GetPmcSignal(ushort handle, short addrKind, short dataType, ushort start, ushort end, ushort dataLength)
        {
            var pmc = new Focas1.IODBPMC0();
            Focas1.pmc_rdpmcrng(handle, addrKind, dataType, start, end, dataLength, pmc);
            return pmc;
        }

        public List<string> GetAllAxisNames(ushort handle, short axisCount = Focas1.MAX_AXIS)
        {
            List<string> result = new();
            var axn = new Focas1.ODBAXISNAME();
            for (short i = 1; i <= axisCount; i++)
            {
                short a = i;
                if (Focas1.cnc_rdaxisname(handle, ref a, axn) == Focas1.EW_OK)
                {
                    var ax = axn.GetName(i);
                    result.Add($"{ax.Item1}{ax.Item2}");
                }
            }
            return result;
        }

        public string GetMode(ushort handle)
        {
            return handle == 0 ? string.Empty : ModeNumberToString(GetStatusData(handle).aut);
        }

        public string GetStatus(ushort handle)
        {
            return handle == 0 ? string.Empty : StatusNumberToString(GetStatusData(handle).run);
        }

        public (int? regPrg, int? unregPrg, int? usedMem, int? unusedMem) GetProgramDataInfo(ushort handle)
        {
            var odbnc = new Focas1.ODBNC_1();
            var ret = Focas1.cnc_rdproginfo(handle, 0, 12, odbnc);
            if (ret != Focas1.EW_OK)
            {
                MessageBoxWindow.Show(ret.ToString(), "GetProgramDataInfo", MessageBoxButton.OK, MessageBoxImage.Error);
                return (null, null, null, null);
            }
            return (odbnc.reg_prg, odbnc.unreg_prg, odbnc.used_mem, odbnc.unused_mem);
        }

        public int GetSpindleLoad(ushort handle, short spindleNumber)
        {
            var buf = new Focas1.ODBSPLOAD();
            var ret = Focas1.cnc_rdspmeter(handle, 0, ref spindleNumber, buf);
            if (ret != Focas1.EW_OK)
            {
                MessageBoxWindow.Show(ret.ToString(), "GetSpindleLoad", MessageBoxButton.OK, MessageBoxImage.Error);
                return 0;
            }
            return buf.spload1.spload.data;
        }

        public string GetProgramName(ushort handle)
        {
            StringBuilder stringBuilder = new StringBuilder();
            //var odbpro = new Focas1.ODBPRO();
            var odbexeprg = new Focas1.ODBEXEPRG();
            //if (Focas1.cnc_rdprgnum(handle, odbpro) == Focas1.EW_OK) { stringBuilder.Append(odbpro.data); }
            if (Focas1.cnc_exeprgname(handle, odbexeprg) == Focas1.EW_OK) { stringBuilder.Append($"N{odbexeprg.o_num:D4} (").Append(odbexeprg.name).Append(")"); }
            return stringBuilder.ToString().Trim();
        }

        public List<string> GetAlarms(ushort handle)
        {
            List<string> alarms = new();

            string[] almmsg = {
                    "P/S 100 ALARM","P/S 000 ALARM",
                    "P/S 101 ALARM","P/S ALARM (1-255)",
                    "OT ALARM",     "OH ALARM",
                    "SERVO ALARM",  "SYSTEM ALARM",
                    "APC ALARM",    "SPINDLE ALARM",
                    "P/S ALARM (5000-)"
            };
            int alm;
            ushort idx;
            Focas1.cnc_alarm2(handle, out alm);
            if (alm == 0)
            {
                return alarms;
            }
            for (idx = 0; idx < 11; idx++)
            {
                if ((alm & 0x0001) > 0)
                {
                    alarms.Add(almmsg[idx]);
                }
                alm >>= 1;
            }
            return alarms;
        }

        private Focas1.ODBST GetStatusData(ushort handle)
        {
            var status = new Focas1.ODBST();
            Focas1.cnc_statinfo(handle, status);
            return status;
        }

        private string ModeNumberToString(int num) => num switch
        {
            0 => "MDI",
            1 => "Memory",
            3 => "Edit",
            4 => "Handle",
            5 => "JOG",
            6 => "Teach in JOG",
            7 => "Teach in HND",
            8 => "INC",
            9 => "REF",
            10 => "RMT",
            _ => "UNAVAILABLE"
        };

        private string StatusNumberToString(int num) => num switch
        {
            0 => "****",
            1 => "STOP",
            2 => "HOLD",
            3 => "STRT",
            4 => "MSTR",
            _ => "UNAVAILABLE"
        };
    }
}
