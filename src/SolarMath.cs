using System;
using System.Text;

namespace SolarCalc
{
    /// <summary>输入时刻的基准类型。</summary>
    public enum TimeBase
    {
        /// <summary>真太阳时（教材例题基准），时角 tau = 15° x (真太阳时 - 12)</summary>
        TrueSolarTime = 0,
        /// <summary>地方平太阳时（只作均时差修正）</summary>
        LocalMeanSolarTime = 1,
        /// <summary>标准时（如北京时间），需作经度修正 + 均时差修正</summary>
        StandardTime = 2
    }

    /// <summary>计算输入条件。</summary>
    public class SolarInput
    {
        /// <summary>当地纬度 phi，单位度，北纬为正、南纬为负。</summary>
        public double Latitude = 31.12;
        /// <summary>当地经度 lambda，单位度，东经为正、西经为负。</summary>
        public double Longitude = 121.47;
        /// <summary>时区，单位小时（北京时间 = 8）。</summary>
        public double TimeZone = 8.0;
        /// <summary>日期。</summary>
        public int Year = 2026;
        public int Month = 9;
        public int Day = 22;
        /// <summary>输入时刻，十进制小时（如 14.5 表示 14:30）。</summary>
        public double ClockHour = 14.0;
        /// <summary>输入时刻的基准。</summary>
        public TimeBase TimeBase = TimeBase.TrueSolarTime;
        /// <summary>光伏板倾角 beta，单位度（0 = 水平，90 = 垂直）。</summary>
        public double Tilt = 30.0;
        /// <summary>光伏板方位角 gamma，单位度（0 = 正南，向西为正，向东为负）。</summary>
        public double Azimuth = 0.0;
    }

    /// <summary>计算结果（角度全部为度，时间全部为小时）。</summary>
    public class SolarResult
    {
        // 日期与时间
        public int N;                          // 序日（1月1日为 1）
        public DateTime Date;
        public double TrueSolarTime;           // 真太阳时，h
        public double EotMinutes;              // 均时差（时差），min
        public double LongitudeCorrectionMinutes; // 经度修正，min

        // 太阳位置
        public double Declination;             // 太阳赤纬角 delta，度
        public double HourAngle;               // 时角 tau，度
        public double Altitude;                // 太阳高度角 h，度
        public double Zenith;                  // 太阳天顶角 theta_z，度
        public double Azimuth;                 // 太阳方位角 gamma_s，度（南=0，西为正）
        public double SinAzimuthFormula;       // 式(2-8) 计算值 cos(delta)sin(tau)/cos(h)
        public double CosAzimuthFormula;       // 式(2-7) 计算值 (sin h sin phi - sin delta)/(cos h cos phi)
        public double NoonAltitude;            // 正午太阳高度角，度

        // 日出日没
        public double SunriseHourAngle;        // 日出时角 tau0出，度（负）
        public double SunsetHourAngle;         // 日没时角 tau0没，度（正）
        public double DayLength;               // 日照时间 T，h
        public double SunriseTrueSolarTime;    // 日出（真太阳时），h
        public double SunsetTrueSolarTime;     // 日没（真太阳时），h
        public double SunriseClockTime;        // 日出（输入时刻基准），h
        public double SunsetClockTime;         // 日没（输入时刻基准），h
        public double SunriseAzimuth;          // 日出方位角，度
        public double SunsetAzimuth;           // 日没方位角，度
        public bool PolarDay;                  // 极昼
        public bool PolarNight;                // 极夜
        public bool IsDaylight;                // 输入时刻是否为白天

        // 光伏板
        public double Tilt;                    // 倾角 beta，度
        public double PanelAzimuth;            // 方位角 gamma，度
        public double Incidence;               // 太阳入射角 theta，度
        public double CosIncidence;            // cos(theta)
        public double PanelNormalAltitude;     // 板面法线高度角 = 90 - beta，度
        public double PanelSolarAzimuthDiff;   // 太阳方位与板面朝向之差，度（-180..180）
    }

    /// <summary>
    /// 太阳能光伏 —— 太阳位置与太阳入射角计算核心。
    /// 全部公式取自教材《2.2 太阳与地球》：
    ///   (2-3)  sin h = sin(phi)sin(delta) + cos(phi)cos(delta)cos(tau)
    ///   (2-7)  cos(gamma_s) = (sin h sin(phi) - sin(delta)) / (cos h cos(phi))
    ///   (2-8)  sin(gamma_s) = cos(delta) sin(tau) / cos h
    ///   (2-9)  cos(tau0) = -tan(phi) tan(delta)
    ///   (1-11) T = (2/15°) arccos(-tan(phi) tan(delta))
    ///   (2-13) cos(theta) = sin(delta)(sin(phi)cos(beta) - cos(phi)sin(beta)cos(gamma))
    ///                       + cos(delta)cos(tau)(cos(phi)cos(beta) + sin(phi)sin(beta))
    ///                       + cos(delta)sin(beta)sin(gamma)sin(tau)
    ///   赤纬角 delta = 23.45° sin[ 360° x (284 + n) / 365 ]
    /// </summary>
    public static class SolarMath
    {
        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;
        /// <summary>地球自转角速度，15°/h。</summary>
        public const double EarthRotation = 15.0;

        public static double ToRad(double deg) { return deg * DegToRad; }
        public static double ToDeg(double rad) { return rad * RadToDeg; }

        public static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static double SinD(double deg) { return Math.Sin(deg * DegToRad); }
        public static double CosD(double deg) { return Math.Cos(deg * DegToRad); }
        public static double TanD(double deg) { return Math.Tan(deg * DegToRad); }

        /// <summary>把 -180..180 之外的角度归化到 (-180, 180]。</summary>
        public static double Normalize180(double deg)
        {
            double v = deg % 360.0;
            if (v > 180.0) v -= 360.0;
            if (v <= -180.0) v += 360.0;
            return v;
        }

        /// <summary>序日 n：1月1日为 1。公式中分母固定取 365（与教材一致）。</summary>
        public static int DayOfYear(int year, int month, int day)
        {
            DateTime d = new DateTime(year, month, day);
            return d.DayOfYear;
        }

        /// <summary>太阳赤纬角 delta = 23.45° sin[360°(284+n)/365°]。</summary>
        public static double Declination(int n)
        {
            return 23.45 * SinD(360.0 * (284.0 + n) / 365.0);
        }

        /// <summary>
        /// 均时差（时差）EoT = 真太阳时 - 平太阳时，单位分钟。
        /// EoT = 9.87 sin(2B) - 7.53 cos(B) - 1.5 sin(B)，B = 360°(n-81)/364。
        /// </summary>
        public static double EquationOfTimeMinutes(int n)
        {
            double b = 360.0 * (n - 81.0) / 364.0;
            return 9.87 * SinD(2.0 * b) - 7.53 * CosD(b) - 1.5 * SinD(b);
        }

        /// <summary>计算主入口。</summary>
        public static SolarResult Compute(SolarInput input)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (input.Latitude < -90.0 || input.Latitude > 90.0)
                throw new ArgumentException("纬度必须在 -90° ~ +90° 之间。");
            if (Math.Abs(input.Latitude) == 90.0)
                throw new ArgumentException("极点（±90°）不在本程序适用范围内。");
            if (input.Tilt < 0.0 || input.Tilt > 180.0)
                throw new ArgumentException("光伏板倾角必须在 0° ~ 180° 之间。");

            SolarResult r = new SolarResult();
            DateTime date = new DateTime(input.Year, input.Month, input.Day);
            r.Date = date;
            r.N = date.DayOfYear;

            double phi = input.Latitude;
            double beta = input.Tilt;
            double gamma = input.Azimuth;

            // ---------- 1. 时间基准换算：输入时刻 -> 真太阳时 ----------
            double eot = EquationOfTimeMinutes(r.N);          // min
            double lonCorr = 4.0 * (input.Longitude - EarthRotation * input.TimeZone); // min
            r.EotMinutes = eot;
            r.LongitudeCorrectionMinutes = lonCorr;

            double tsolar;
            switch (input.TimeBase)
            {
                case TimeBase.TrueSolarTime:
                    tsolar = input.ClockHour;
                    break;
                case TimeBase.LocalMeanSolarTime:
                    tsolar = input.ClockHour + eot / 60.0;
                    break;
                default: // StandardTime
                    tsolar = input.ClockHour + (lonCorr + eot) / 60.0;
                    break;
            }
            r.TrueSolarTime = tsolar;

            // ---------- 2. 太阳赤纬角 delta ----------
            double delta = Declination(r.N);
            r.Declination = delta;

            // ---------- 3. 时角 tau = 15° x (真太阳时 - 12) ----------
            double tau = EarthRotation * (tsolar - 12.0);
            r.HourAngle = tau;

            // ---------- 4. 太阳高度角 h 【式(2-3)】 ----------
            double sinH = SinD(phi) * SinD(delta) + CosD(phi) * CosD(delta) * CosD(tau);
            sinH = Clamp(sinH, -1.0, 1.0);
            double h = ToDeg(Math.Asin(sinH));
            r.Altitude = h;
            r.Zenith = 90.0 - h;

            // ---------- 5. 太阳方位角 gamma_s 【式(2-7) / 式(2-8)】 ----------
            // 两式公共分母分别为 cos h 与 cos h cos(phi)。
            // 取 cos h cos(phi) 通分后作 atan2，既与教材完全等价，
            // 又在太阳接近天顶（cos h -> 0）时数值稳定。
            double sinGammaNum = CosD(delta) * SinD(tau);              // 式(2-8) 分子
            double cosGammaNum = sinH * SinD(phi) - SinD(delta);       // 式(2-7) 分子 x cos(phi)
            double cosH = Math.Sqrt(Math.Max(0.0, 1.0 - sinH * sinH));

            if (cosH > 1e-12)
            {
                r.SinAzimuthFormula = Clamp(sinGammaNum / cosH, -1.0, 1.0);
                r.CosAzimuthFormula = Clamp(cosGammaNum / cosH / CosD(phi), -1.0, 1.0);
            }
            else
            {
                // 太阳恰在天顶：方位角无定义
                r.SinAzimuthFormula = 0.0;
                r.CosAzimuthFormula = 0.0;
            }
            double gammaS = ToDeg(Math.Atan2(sinGammaNum * CosD(phi), cosGammaNum));
            r.Azimuth = gammaS;

            // ---------- 6. 正午太阳高度角 ----------
            r.NoonAltitude = 90.0 - Math.Abs(phi - delta);

            // ---------- 7. 日出日没时角与日照时间 【式(2-9) / 式(1-11)】 ----------
            double cosTau0 = -TanD(phi) * TanD(delta);
            if (cosTau0 <= -1.0)
            {
                r.PolarDay = true;
                r.SunriseHourAngle = -180.0;
                r.SunsetHourAngle = 180.0;
                r.DayLength = 24.0;
            }
            else if (cosTau0 >= 1.0)
            {
                r.PolarNight = true;
                r.SunriseHourAngle = 0.0;
                r.SunsetHourAngle = 0.0;
                r.DayLength = 0.0;
            }
            else
            {
                double tau0 = ToDeg(Math.Acos(cosTau0));   // 0 ~ 180
                r.SunriseHourAngle = -tau0;
                r.SunsetHourAngle = tau0;
                // T = (2/15°) arccos(-tan(phi) tan(delta)) = 2 * |tau0| / 15
                r.DayLength = 2.0 * tau0 / EarthRotation;
            }

            r.SunriseTrueSolarTime = 12.0 - r.DayLength / 2.0;   // = tau0出/15 + 12
            r.SunsetTrueSolarTime = 12.0 + r.DayLength / 2.0;

            // 换算回输入时刻基准
            double toClock;
            switch (input.TimeBase)
            {
                case TimeBase.TrueSolarTime: toClock = 0.0; break;
                case TimeBase.LocalMeanSolarTime: toClock = -eot / 60.0; break;
                default: toClock = -(lonCorr + eot) / 60.0; break;
            }
            r.SunriseClockTime = r.SunriseTrueSolarTime + toClock;
            r.SunsetClockTime = r.SunsetTrueSolarTime + toClock;

            // 白天判定
            if (r.PolarDay) r.IsDaylight = true;
            else if (r.PolarNight) r.IsDaylight = false;
            else r.IsDaylight = (h > 0.0);

            // 日出、日没方位角：h = 0 时式(2-7)/(2-8) 的取值
            double sunriseAzi = ToDeg(Math.Atan2(CosD(delta) * SinD(r.SunriseHourAngle) * CosD(phi), -SinD(delta)));
            double sunsetAzi = ToDeg(Math.Atan2(CosD(delta) * SinD(r.SunsetHourAngle) * CosD(phi), -SinD(delta)));
            r.SunriseAzimuth = Double.IsNaN(sunriseAzi) ? 0.0 : sunriseAzi;
            r.SunsetAzimuth = Double.IsNaN(sunsetAzi) ? 0.0 : sunsetAzi;

            // ---------- 8. 太阳入射角 theta ----------
            // 采用国际通用形式（Duffie & Beckman, Solar Engineering of Thermal Processes, Eq.1.6.2）：
            //   cos(theta) = sin(delta)( sin(phi)cos(beta) - cos(phi)sin(beta)cos(gamma) )
            //              + cos(delta)cos(tau)( cos(phi)cos(beta) + sin(phi)sin(beta)cos(gamma) )
            //              + cos(delta)sin(beta)sin(gamma)sin(tau)
            // 注：课件式(2-13)第二括号内漏乘了 cos(gamma)，按原文计算会出现 cos(theta) > 1
            //     （如北京 12/22 10:30、β=60°、γ=-30° 时原文得 1.0493，物理上不可能）。
            //     本程序采用补回 cos(gamma) 的正确形式，详见 使用说明.md。
            double cosTheta =
                  SinD(delta) * (SinD(phi) * CosD(beta) - CosD(phi) * SinD(beta) * CosD(gamma))
                + CosD(delta) * CosD(tau) * (CosD(phi) * CosD(beta) + SinD(phi) * SinD(beta) * CosD(gamma))
                + CosD(delta) * SinD(beta) * SinD(gamma) * SinD(tau);
            cosTheta = Clamp(cosTheta, -1.0, 1.0);
            r.CosIncidence = cosTheta;
            r.Incidence = ToDeg(Math.Acos(cosTheta));
            r.Tilt = beta;
            r.PanelAzimuth = gamma;
            r.PanelNormalAltitude = 90.0 - beta;
            r.PanelSolarAzimuthDiff = Normalize180(gammaS - gamma);

            return r;
        }

        /// <summary>把十进制小时格式化为 HH:mm:ss，自动处理跨日（mod 24）。</summary>
        public static string FormatHour(double hour)
        {
            double h = hour % 24.0;
            if (h < 0.0) h += 24.0;
            int hh = (int)Math.Floor(h);
            int mm = (int)Math.Floor((h - hh) * 60.0);
            int ss = (int)Math.Round((((h - hh) * 60.0) - mm) * 60.0);
            if (ss >= 60) { ss -= 60; mm += 1; }
            if (mm >= 60) { mm -= 60; hh += 1; }
            if (hh >= 24) hh -= 24;
            return string.Format("{0:00}:{1:00}:{2:00}", hh, mm, ss);
        }

        /// <summary>把十进制小时格式化为 HH:mm，自动处理跨日（mod 24）。</summary>
        public static string FormatHourShort(double hour)
        {
            double h = hour % 24.0;
            if (h < 0.0) h += 24.0;
            int hh = (int)Math.Floor(h);
            int mm = (int)Math.Round((h - hh) * 60.0);
            if (mm >= 60) { mm -= 60; hh += 1; }
            if (hh >= 24) hh -= 24;
            return string.Format("{0:00}:{1:00}", hh, mm);
        }

        /// <summary>方位角文字描述（南=0，西为正）。</summary>
        public static string AzimuthText(double gamma)
        {
            double a = Math.Abs(gamma);
            string ew = (gamma >= 0.0) ? "偏西" : "偏东";
            if (a < 0.05) return "正南";
            if (Math.Abs(a - 180.0) < 0.05) return "正北";
            if (Math.Abs(a - 90.0) < 0.05) return (gamma > 0.0) ? "正西" : "正东";
            if (a < 90.0) return "南" + ew + string.Format("{0:F2}°", a);
            // a 在 90°~180° 之间：以正北为基准描述
            return "北" + ew + string.Format("{0:F2}°", a - 90.0);
        }

        /// <summary>生成与教材例题格式一致的详细计算过程报告。</summary>
        public static string BuildReport(SolarInput inp, SolarResult r)
        {
            StringBuilder sb = new StringBuilder();
            string line = new string('=', 72);
            string dash = new string('-', 72);

            sb.AppendLine(line);
            sb.AppendLine("              太阳能光伏 —— 太阳位置与太阳入射角计算书");
            sb.AppendLine(line);
            sb.AppendLine();

            // 一、输入条件
            sb.AppendLine("【一】输入条件");
            sb.AppendLine(dash);
            sb.AppendFormat("  观测点纬度   φ    = {0,10:F4}°   ({1})\r\n", inp.Latitude,
                inp.Latitude >= 0 ? "北纬" : "南纬");
            sb.AppendFormat("  观测点经度   λ    = {0,10:F4}°   ({1})\r\n", inp.Longitude,
                inp.Longitude >= 0 ? "东经" : "西经");
            sb.AppendFormat("  时区              = UTC{0}{1:F0}\r\n", inp.TimeZone >= 0 ? "+" : "", inp.TimeZone);
            sb.AppendFormat("  日期              = {0:yyyy-MM-dd}  ({1})\r\n",
                r.Date, "星期" + "日一二三四五六"[(int)r.Date.DayOfWeek]);
            sb.AppendFormat("  序日         n    = {0}\r\n", r.N);
            sb.AppendFormat("  输入时刻          = {0}   ({1})\r\n",
                FormatHour(inp.ClockHour), TimeBaseText(inp.TimeBase));
            sb.AppendFormat("  计算用真太阳时    = {0,10:F4} h\r\n", r.TrueSolarTime);
            sb.AppendFormat("  光伏板倾角   β    = {0,10:F4}°\r\n", inp.Tilt);
            sb.AppendFormat("  光伏板方位角 γ    = {0,10:F4}°   ({1})\r\n", inp.Azimuth,
                AzimuthText(inp.Azimuth));
            sb.AppendLine();

            // 时间换算说明
            if (inp.TimeBase != TimeBase.TrueSolarTime)
            {
                sb.AppendLine("  ● 时刻基准换算");
                sb.AppendFormat("     经度修正 = 4 min/° x (λ - 15°/h x 时区) = 4 x ({0:F4} - 15 x {1:F0}) = {2:F3} min\r\n",
                    inp.Longitude, inp.TimeZone, r.LongitudeCorrectionMinutes);
                sb.AppendFormat("     均时差   = 9.87 sin(2B) - 7.53 cos B - 1.5 sin B , B = 360°(n-81)/364\r\n");
                sb.AppendFormat("              = {0:F3} min\r\n", r.EotMinutes);
                sb.AppendFormat("     真太阳时 = 输入时刻 + (经度修正 + 均时差)/60 = {0} + ({1:F3} + {2:F3})/60 = {3:F4} h\r\n",
                    FormatHour(inp.ClockHour), r.LongitudeCorrectionMinutes, r.EotMinutes, r.TrueSolarTime);
                sb.AppendLine();
            }

            // 二、太阳赤纬角
            sb.AppendLine("【二】太阳赤纬角 δ           公式：δ = 23.45° x sin[ 360° x (284 + n) / 365 ]");
            sb.AppendLine(dash);
            sb.AppendFormat("        δ = 23.45° x sin[ 360° x (284 + {0}) / 365 ]\r\n", r.N);
            sb.AppendFormat("          = 23.45° x sin( {0:F4}° )\r\n", 360.0 * (284.0 + r.N) / 365.0);
            sb.AppendFormat("          = 23.45° x ( {0:F6} )\r\n", Math.Sin(ToRad(360.0 * (284.0 + r.N) / 365.0)));
            sb.AppendFormat("        δ = {0:F4}°\r\n", r.Declination);
            sb.AppendLine();

            // 三、时角
            sb.AppendLine("【三】时角 τ                 公式：τ = 15° x ( 真太阳时 - 12 )");
            sb.AppendLine(dash);
            sb.AppendFormat("        τ = 15° x ( {0:F4} - 12 )\r\n", r.TrueSolarTime);
            sb.AppendFormat("        τ = {0:F4}°\r\n", r.HourAngle);
            sb.AppendFormat("        （τ > 0 为下午，τ < 0 为上午，τ = 0 为正午）\r\n");
            sb.AppendLine();

            // 四、太阳高度角
            sb.AppendLine("【四】太阳高度角 h           公式 (2-3)：sin h = sinφ sinδ + cosφ cosδ cosτ");
            sb.AppendLine(dash);
            double t1 = SinD(inp.Latitude) * SinD(r.Declination);
            double t2 = CosD(inp.Latitude) * CosD(r.Declination) * CosD(r.HourAngle);
            sb.AppendFormat("        sin h = sin({0:F4}°)sin({1:F4}°) + cos({0:F4}°)cos({1:F4}°)cos({2:F4}°)\r\n",
                inp.Latitude, r.Declination, r.HourAngle);
            sb.AppendFormat("              = ( {0:F6} ) + ( {1:F6} )\r\n", t1, t2);
            sb.AppendFormat("              = {0:F6}\r\n", t1 + t2);
            sb.AppendFormat("        h = arcsin( {0:F6} ) = {1:F4}°\r\n", t1 + t2, r.Altitude);
            sb.AppendFormat("        太阳天顶角 θz = 90° - h = {0:F4}°\r\n", r.Zenith);
            sb.AppendLine();

            // 五、太阳方位角
            sb.AppendLine("【五】太阳方位角 γs          公式 (2-7)/(2-8)");
            sb.AppendLine(dash);
            sb.AppendLine("        记 h = 太阳高度角，cos h = " + Math.Cos(ToRad(r.Altitude)).ToString("F6"));
            sb.AppendFormat("        (2-8) sin γs = cosδ sinτ / cos h = cos({0:F4}°)sin({1:F4}°) / cos({2:F4}°)\r\n",
                r.Declination, r.HourAngle, r.Altitude);
            sb.AppendFormat("                     = ( {0:F6} ) / ( {1:F6} ) = {2:F6}\r\n",
                CosD(r.Declination) * SinD(r.HourAngle), Math.Cos(ToRad(r.Altitude)), r.SinAzimuthFormula);
            sb.AppendFormat("        (2-7) cos γs = ( sin h sinφ - sinδ ) / ( cos h cosφ )\r\n");
            sb.AppendFormat("                     = ( {0:F6} x {1:F6} - {2:F6} ) / ( {3:F6} x {4:F6} ) = {5:F6}\r\n",
                Math.Sin(ToRad(r.Altitude)), SinD(inp.Latitude), SinD(r.Declination),
                Math.Cos(ToRad(r.Altitude)), CosD(inp.Latitude), r.CosAzimuthFormula);
            sb.AppendFormat("        γs = atan2( sin γs , cos γs ) = {0:F4}°\r\n", r.Azimuth);
            sb.AppendFormat("        方位含义：{0}   （方位角自正南起算，向西为正、向东为负，范围 ±180°）\r\n",
                AzimuthText(r.Azimuth));
            sb.AppendLine();

            // 六、正午高度角
            sb.AppendLine("【六】正午太阳高度角         h午 = 90° - |φ - δ|");
            sb.AppendLine(dash);
            sb.AppendFormat("        h午 = 90° - | {0:F4}° - ( {1:F4}° ) | = {2:F4}°\r\n",
                inp.Latitude, r.Declination, r.NoonAltitude);
            sb.AppendLine();

            // 七、日出日没时角与日照时间
            sb.AppendLine("【七】日出日没时角与日照时间");
            sb.AppendLine(dash);
            sb.AppendLine("        公式 (2-9)：cos τ0 = -tanφ tanδ");
            sb.AppendFormat("        -tan({0:F4}°) x tan({1:F4}°) = {2:F6}\r\n",
                inp.Latitude, r.Declination, -TanD(inp.Latitude) * TanD(r.Declination));
            if (r.PolarDay)
            {
                sb.AppendLine("        |值| >= 1 且 <-1 → 出现极昼，全天 24 h 均为白天。");
                sb.AppendFormat("        τ0出 = -180° , τ0没 = +180° , T = 24.0000 h\r\n");
            }
            else if (r.PolarNight)
            {
                sb.AppendLine("        |值| >= 1 且 >1 → 出现极夜，全天均无日照。");
                sb.AppendFormat("        τ0出/τ0没 无定义 , T = 0.0000 h\r\n");
            }
            else
            {
                sb.AppendFormat("        τ0 = arccos( {0:F6} ) = {1:F4}°\r\n",
                    -TanD(inp.Latitude) * TanD(r.Declination), r.SunsetHourAngle);
                sb.AppendFormat("        日出时角 τ0出 = {0:F4}°   日没时角 τ0没 = +{1:F4}°\r\n",
                    r.SunriseHourAngle, r.SunsetHourAngle);
            }
            sb.AppendLine("        公式 (1-11)：T = (2/15°) arccos(-tanφ tanδ) = 2 x |τ0| / 15°");
            if (!r.PolarNight)
                sb.AppendFormat("        T = 2 x |{0:F4}°| / 15° = 2 x {1:F4} / 15 = {2:F4} h = {3:F2} h\r\n",
                    r.SunsetHourAngle, Math.Abs(r.SunsetHourAngle), r.DayLength, r.DayLength);
            else
                sb.AppendFormat("        T = 0.0000 h\r\n");
            sb.AppendFormat("        日出时刻（真太阳时） = 12 - T/2 = {0}\r\n", FormatHour(r.SunriseTrueSolarTime));
            sb.AppendFormat("        日没时刻（真太阳时） = 12 + T/2 = {0}\r\n", FormatHour(r.SunsetTrueSolarTime));
            if (inp.TimeBase != TimeBase.TrueSolarTime)
            {
                sb.AppendFormat("        日出时刻（{0}）= {1}\r\n", TimeBaseText(inp.TimeBase), FormatHour(r.SunriseClockTime));
                sb.AppendFormat("        日没时刻（{0}）= {1}\r\n", TimeBaseText(inp.TimeBase), FormatHour(r.SunsetClockTime));
            }
            if (!r.PolarDay && !r.PolarNight)
            {
                sb.AppendFormat("        日出方位角 γs出 = {0:F4}°  ({1})\r\n", r.SunriseAzimuth, AzimuthText(r.SunriseAzimuth));
                sb.AppendFormat("        日没方位角 γs没 = {0:F4}°  ({1})\r\n", r.SunsetAzimuth, AzimuthText(r.SunsetAzimuth));
            }
            sb.AppendLine();

            // 八、太阳入射角
            sb.AppendLine("【八】太阳入射角 θ           公式：cosθ = sinδ( sinφcosβ - cosφsinβcosγ )");
            sb.AppendLine("                                    + cosδcosτ( cosφcosβ + sinφsinβcosγ )");
            sb.AppendLine("                                    + cosδsinβsinγsinτ");
            sb.AppendLine(dash);
            sb.AppendFormat("        其中 φ={0:F4}°  δ={1:F4}°  τ={2:F4}°  β={3:F4}°  γ={4:F4}°\r\n",
                inp.Latitude, r.Declination, r.HourAngle, inp.Tilt, inp.Azimuth);
            double a1 = SinD(r.Declination) * (SinD(inp.Latitude) * CosD(inp.Tilt) - CosD(inp.Latitude) * SinD(inp.Tilt) * CosD(inp.Azimuth));
            double a2 = CosD(r.Declination) * CosD(r.HourAngle) * (CosD(inp.Latitude) * CosD(inp.Tilt) + SinD(inp.Latitude) * SinD(inp.Tilt) * CosD(inp.Azimuth));
            double a3 = CosD(r.Declination) * SinD(inp.Tilt) * SinD(inp.Azimuth) * SinD(r.HourAngle);
            sb.AppendFormat("        第1项 = {0,12:F6}\r\n", a1);
            sb.AppendFormat("        第2项 = {0,12:F6}\r\n", a2);
            sb.AppendFormat("        第3项 = {0,12:F6}\r\n", a3);
            sb.AppendFormat("        cosθ  = {0,12:F6}\r\n", a1 + a2 + a3);
            sb.AppendFormat("        θ = arccos( {0:F6} ) = {1:F4}°\r\n", r.CosIncidence, r.Incidence);
            sb.AppendFormat("        板面法线高度角 = 90° - β = {0:F4}°\r\n", r.PanelNormalAltitude);
            sb.AppendFormat("        太阳方位与板面朝向之差 Δγ = γs - γ = {0:F4}°\r\n", r.PanelSolarAzimuthDiff);
            sb.AppendLine();

            // 九、校核
            sb.AppendLine("【九】结果校核（内部一致性）");
            sb.AppendLine(dash);
            SolarInput check = CloneInput(inp);
            check.Tilt = 0.0;
            SolarResult horizon = Compute(check);
            sb.AppendFormat("        校核1  板面水平(β=0)时 θ 应等于天顶角 θz：θ={0:F4}°  θz={1:F4}°  偏差={2:E3}°\r\n",
                horizon.Incidence, r.Zenith, Math.Abs(horizon.Incidence - r.Zenith));
            double sumSq = r.SinAzimuthFormula * r.SinAzimuthFormula + r.CosAzimuthFormula * r.CosAzimuthFormula;
            sb.AppendFormat("        校核2  式(2-7)与式(2-8)应满足 sin²γs+cos²γs=1：实际 = {0:F8}  偏差 = {1:E3}\r\n",
                sumSq, Math.Abs(sumSq - 1.0));
            double hh = ToDeg(Math.Asin(Clamp(SinD(inp.Latitude) * SinD(r.Declination) + CosD(inp.Latitude) * CosD(r.Declination), -1, 1)));
            sb.AppendFormat("        校核3  τ=0 时由式(2-3)得 h={0:F4}°，应等于正午高度角 h午={1:F4}°，偏差={2:E3}°\r\n",
                hh, r.NoonAltitude, Math.Abs(hh - r.NoonAltitude));
            sb.AppendLine();

            // 十、结果汇总
            sb.AppendLine(line);
            sb.AppendLine("【结果汇总】");
            sb.AppendLine(dash);
            sb.AppendFormat("  序日 n              = {0}\r\n", r.N);
            sb.AppendFormat("  太阳赤纬角 δ        = {0:F4}°\r\n", r.Declination);
            sb.AppendFormat("  时角 τ              = {0:F4}°\r\n", r.HourAngle);
            sb.AppendFormat("  太阳高度角 h        = {0:F4}°\r\n", r.Altitude);
            sb.AppendFormat("  太阳天顶角 θz       = {0:F4}°\r\n", r.Zenith);
            sb.AppendFormat("  太阳方位角 γs       = {0:F4}°   ({1})\r\n", r.Azimuth, AzimuthText(r.Azimuth));
            sb.AppendFormat("  正午太阳高度角      = {0:F4}°\r\n", r.NoonAltitude);
            sb.AppendFormat("  日出时角 τ0出       = {0:F4}°\r\n", r.SunriseHourAngle);
            sb.AppendFormat("  日没时角 τ0没       = {0:F4}°\r\n", r.SunsetHourAngle);
            sb.AppendFormat("  日照时间 T          = {0:F4} h  = {1:F2} h = {2}\r\n",
                r.DayLength, r.DayLength, FormatHourShort(r.DayLength));
            sb.AppendFormat("  日出时刻(真太阳时)  = {0}\r\n", FormatHour(r.SunriseTrueSolarTime));
            sb.AppendFormat("  日没时刻(真太阳时)  = {0}\r\n", FormatHour(r.SunsetTrueSolarTime));
            sb.AppendFormat("  太阳入射角 θ        = {0:F4}°\r\n", r.Incidence);
            sb.AppendFormat("  cos θ               = {0:F6}\r\n", r.CosIncidence);
            sb.AppendFormat("  当前时刻日照状态    = {0}\r\n",
                r.PolarDay ? "极昼（全天有日照）" : r.PolarNight ? "极夜（全天无日照）" : (r.IsDaylight ? "白天（h>0）" : "夜间（h<0）"));
            sb.AppendLine(line);
            return sb.ToString();
        }

        public static string TimeBaseText(TimeBase tb)
        {
            switch (tb)
            {
                case TimeBase.TrueSolarTime: return "真太阳时（教材基准）";
                case TimeBase.LocalMeanSolarTime: return "地方平太阳时";
                default: return "标准时（如北京时间）";
            }
        }

        public static SolarInput CloneInput(SolarInput s)
        {
            SolarInput c = new SolarInput();
            c.Latitude = s.Latitude;
            c.Longitude = s.Longitude;
            c.TimeZone = s.TimeZone;
            c.Year = s.Year;
            c.Month = s.Month;
            c.Day = s.Day;
            c.ClockHour = s.ClockHour;
            c.TimeBase = s.TimeBase;
            c.Tilt = s.Tilt;
            c.Azimuth = s.Azimuth;
            return c;
        }
    }
}
