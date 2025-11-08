using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models.Reports
{
    public readonly struct Qualification
    {
        public Qualification(int value, double efficiencyValueHH, double efficiencyCoefficientHH, double efficiencyValueH, double efficiencyCoefficientH, double efficiencyValueN, double efficiencyCoefficientN, double efficiencyValueL, double efficiencyCoefficientL, double efficiencyValueLL, double efficiencyCoefficientLL, double efficiencyValueLLL, double efficiencyCoefficientLLL, double downTimesValueHH, double downTimesCoefficientHH, double downTimesValueH, double downTimesCoefficientH, double downTimesValueN, double downTimesCoefficientN, double downTimesValueL, double downTimesCoefficientL, double downTimesValueLL, double downTimesCoefficientLL, double downTimesValueLLL, double downTimesCoefficientLLL)
        {
            Value = value;
            EfficiencyValueHH = efficiencyValueHH;
            EfficiencyCoefficientHH = efficiencyCoefficientHH;
            EfficiencyValueH = efficiencyValueH;
            EfficiencyCoefficientH = efficiencyCoefficientH;
            EfficiencyValueN = efficiencyValueN;
            EfficiencyCoefficientN = efficiencyCoefficientN;
            EfficiencyValueL = efficiencyValueL;
            EfficiencyCoefficientL = efficiencyCoefficientL;
            EfficiencyValueLL = efficiencyValueLL;
            EfficiencyCoefficientLL = efficiencyCoefficientLL;
            EfficiencyValueLLL = efficiencyValueLLL;
            EfficiencyCoefficientLLL = efficiencyCoefficientLLL;
            DownTimesValueHH = downTimesValueHH;
            DownTimesCoefficientHH = downTimesCoefficientHH;
            DownTimesValueH = downTimesValueH;
            DownTimesCoefficientH = downTimesCoefficientH;
            DownTimesValueN = downTimesValueN;
            DownTimesCoefficientN = downTimesCoefficientN;
            DownTimesValueL = downTimesValueL;
            DownTimesCoefficientL = downTimesCoefficientL;
            DownTimesValueLL = downTimesValueLL;
            DownTimesCoefficientLL = downTimesCoefficientLL;
            DownTimesValueLLL = downTimesValueLLL;
            DownTimesCoefficientLLL = downTimesCoefficientLLL;
        }

        public int Value { get;}

        public double EfficiencyValueHH { get;}
        public double EfficiencyCoefficientHH { get;}

        public double EfficiencyValueH { get;}
        public double EfficiencyCoefficientH { get;}

        public double EfficiencyValueN { get;}
        public double EfficiencyCoefficientN { get;}

        public double EfficiencyValueL { get;}
        public double EfficiencyCoefficientL { get;}

        public double EfficiencyValueLL { get;}
        public double EfficiencyCoefficientLL { get;}

        public double EfficiencyValueLLL { get;}
        public double EfficiencyCoefficientLLL { get;}

        public double DownTimesValueHH { get;}
        public double DownTimesCoefficientHH { get;}

        public double DownTimesValueH { get;}
        public double DownTimesCoefficientH { get;}

        public double DownTimesValueN { get;}
        public double DownTimesCoefficientN { get;}

        public double DownTimesValueL { get;}
        public double DownTimesCoefficientL { get;}

        public double DownTimesValueLL { get;}
        public double DownTimesCoefficientLL { get;}

        public double DownTimesValueLLL { get;}
        public double DownTimesCoefficientLLL { get;}
    }
}
