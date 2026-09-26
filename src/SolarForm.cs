using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SolarCalc
{
    // ==================================================================
    //  日照曲线绘图面板：绘制全天 太阳高度角 h 与 太阳入射角 θ 曲线
    // ==================================================================
    public class SolarChartPanel : Panel
    {
        private SolarInput input;
        private SolarResult current;
        private const double YMin = -90.0;
        private const double YMax = 180.0;
        private const double Step = 0.05;      // 采样步长 h

        public SolarChartPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
            this.BackColor = Color.White;
        }

        public void SetData(SolarInput inp, SolarResult cur)
        {
            input = inp;
            current = cur;
            Invalidate();
        }

        private float X(RectangleF p, double t) { return p.Left + (float)(t / 24.0 * p.Width); }
        private float Y(RectangleF p, double deg) { return p.Bottom - (float)((deg - YMin) / (YMax - YMin) * p.Height); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.White);

            if (input == null || current == null)
            {
                TextRenderer.DrawText(g, "请输入参数后点击［计 算］，此处显示全天日照曲线",
                    this.Font, this.ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            RectangleF plot = new RectangleF(66f, 30f, this.Width - 66f - 22f, this.Height - 30f - 46f);
            if (plot.Width < 80f || plot.Height < 60f) return;

            using (Font fAxis = new Font(this.Font.FontFamily, 8.5f))
            using (Font fLegend = new Font(this.Font.FontFamily, 9f))
            using (Pen penGrid = new Pen(Color.FromArgb(226, 232, 240), 1f))
            using (Pen penAxis = new Pen(Color.FromArgb(120, 130, 145), 1.4f))
            using (Pen penZero = new Pen(Color.FromArgb(150, 160, 175), 1.2f))
            using (Pen penH = new Pen(Color.FromArgb(28, 88, 190), 2.2f))
            using (Pen penT = new Pen(Color.FromArgb(205, 60, 60), 2.2f))
            using (Pen penNow = new Pen(Color.FromArgb(60, 60, 60), 1.5f))
            using (Pen penRise = new Pen(Color.FromArgb(230, 150, 30), 1.2f))
            using (Brush brDay = new SolidBrush(Color.FromArgb(255, 249, 214)))
            using (Brush brAxisText = new SolidBrush(Color.FromArgb(90, 100, 115)))
            {
                penNow.DashStyle = DashStyle.Dash;
                penRise.DashStyle = DashStyle.Dot;

                // ---- 采样计算全天曲线（x 轴统一为真太阳时） ----
                int n = (int)Math.Round(24.0 / Step) + 1;
                double[] hArr = new double[n];
                double[] tArr = new double[n];
                SolarInput s = SolarMath.CloneInput(input);
                s.TimeBase = TimeBase.TrueSolarTime;
                for (int i = 0; i < n; i++)
                {
                    double hr = i * Step;
                    s.ClockHour = hr;
                    SolarResult rr = SolarMath.Compute(s);
                    hArr[i] = rr.Altitude;
                    tArr[i] = rr.Incidence;
                }

                Region oldClip = g.Clip;
                g.SetClip(plot, CombineMode.Intersect);

                // ---- 白昼底色 ----
                int k = 0;
                while (k < n)
                {
                    if (hArr[k] > 0.0)
                    {
                        int j = k;
                        List<PointF> poly = new List<PointF>();
                        poly.Add(new PointF(X(plot, k * Step), Y(plot, 0.0)));
                        while (j < n && hArr[j] > 0.0)
                        {
                            poly.Add(new PointF(X(plot, j * Step), Y(plot, hArr[j])));
                            j++;
                        }
                        poly.Add(new PointF(X(plot, (j - 1) * Step), Y(plot, 0.0)));
                        g.FillPolygon(brDay, poly.ToArray());
                        k = j;
                    }
                    else k++;
                }

                // ---- 网格 ----
                for (int hr = 0; hr <= 24; hr += 2)
                {
                    float x = X(plot, hr);
                    g.DrawLine(penGrid, x, plot.Top, x, plot.Bottom);
                }
                for (int d = -90; d <= 180; d += 30)
                {
                    float y = Y(plot, d);
                    g.DrawLine(d == 0 ? penZero : penGrid, plot.Left, y, plot.Right, y);
                }

                // ---- 日出 / 日没 竖线 ----
                if (!current.PolarDay && !current.PolarNight)
                {
                    float xs = X(plot, current.SunriseTrueSolarTime);
                    float xe = X(plot, current.SunsetTrueSolarTime);
                    g.DrawLine(penRise, xs, plot.Top, xs, plot.Bottom);
                    g.DrawLine(penRise, xe, plot.Top, xe, plot.Bottom);
                }

                // ---- 高度角曲线 ----
                PointF[] ph = new PointF[n];
                PointF[] pt = new PointF[n];
                for (int i = 0; i < n; i++)
                {
                    ph[i] = new PointF(X(plot, i * Step), Y(plot, hArr[i]));
                    pt[i] = new PointF(X(plot, i * Step), Y(plot, tArr[i]));
                }
                g.DrawLines(penT, pt);
                g.DrawLines(penH, ph);

                // ---- 当前时刻标记 ----
                float xnow = X(plot, current.TrueSolarTime);
                g.DrawLine(penNow, xnow, plot.Top, xnow, plot.Bottom);
                g.FillEllipse(Brushes.White, xnow - 5f, Y(plot, current.Altitude) - 5f, 10f, 10f);
                g.DrawEllipse(penH, xnow - 5f, Y(plot, current.Altitude) - 5f, 10f, 10f);
                g.FillEllipse(Brushes.White, xnow - 5f, Y(plot, current.Incidence) - 5f, 10f, 10f);
                g.DrawEllipse(penT, xnow - 5f, Y(plot, current.Incidence) - 5f, 10f, 10f);

                g.Clip = oldClip;

                // ---- 坐标轴 ----
                g.DrawRectangle(penAxis, plot.Left, plot.Top, plot.Width, plot.Height);
                for (int hr = 0; hr <= 24; hr += 2)
                {
                    float x = X(plot, hr);
                    g.DrawLine(penAxis, x, plot.Bottom, x, plot.Bottom + 4);
                    TextRenderer.DrawText(g, hr.ToString(), fAxis,
                        new Point((int)x - 8, (int)plot.Bottom + 6), Color.FromArgb(90, 100, 115));
                }
                for (int d = -90; d <= 180; d += 30)
                {
                    float y = Y(plot, d);
                    g.DrawLine(penAxis, plot.Left - 4, y, plot.Left, y);
                    Size sz = TextRenderer.MeasureText(d.ToString(), fAxis);
                    TextRenderer.DrawText(g, d.ToString(), fAxis,
                        new Point((int)plot.Left - 8 - sz.Width, (int)y - sz.Height / 2), Color.FromArgb(90, 100, 115));
                }

                // ---- 轴标题 ----
                TextRenderer.DrawText(g, "真太阳时 (h)", fAxis,
                    new Point((int)(plot.Left + plot.Width / 2) - 34, (int)plot.Bottom + 26),
                    Color.FromArgb(70, 80, 95));
                string yTitle = "角度 (°)";
                Size ys = TextRenderer.MeasureText(yTitle, fAxis);
                g.DrawString(yTitle, fAxis, brAxisText, 6f, plot.Top - 2f);

                // ---- 图例 ----
                float lx = plot.Left + 10f;
                float ly = plot.Top + 8f;
                g.DrawLine(penH, lx, ly + 6f, lx + 24f, ly + 6f);
                TextRenderer.DrawText(g, "太阳高度角 h", fLegend, new Point((int)lx + 30, (int)ly), Color.FromArgb(28, 88, 190));
                ly += 20f;
                g.DrawLine(penT, lx, ly + 6f, lx + 24f, ly + 6f);
                TextRenderer.DrawText(g, "太阳入射角 θ（光伏板）", fLegend, new Point((int)lx + 30, (int)ly), Color.FromArgb(205, 60, 60));
                ly += 20f;
                g.FillRectangle(brDay, lx, ly + 1f, 24f, 11f);
                g.DrawRectangle(penGrid, lx, ly + 1f, 24f, 11f);
                TextRenderer.DrawText(g, "白昼时段 (h>0)", fLegend, new Point((int)lx + 30, (int)ly), Color.FromArgb(120, 110, 70));
                if (!current.PolarDay && !current.PolarNight)
                {
                    ly += 20f;
                    g.DrawLine(penRise, lx, ly + 6f, lx + 24f, ly + 6f);
                    TextRenderer.DrawText(g, string.Format("日出 {0} / 日没 {1}",
                        SolarMath.FormatHourShort(current.SunriseTrueSolarTime),
                        SolarMath.FormatHourShort(current.SunsetTrueSolarTime)),
                        fLegend, new Point((int)lx + 30, (int)ly), Color.FromArgb(190, 120, 20));
                }

                // ---- 右上角工况 ----
                string info = string.Format("φ={0:F2}°  δ={1:F2}°  β={2:F2}°  γ={3:F2}°   当前 θ={4:F2}°",
                    input.Latitude, current.Declination, input.Tilt, input.Azimuth, current.Incidence);
                Size isz = TextRenderer.MeasureText(info, fLegend);
                TextRenderer.DrawText(g, info, fLegend,
                    new Point((int)(plot.Right - isz.Width), (int)plot.Top - 22), Color.FromArgb(70, 80, 95));
            }
        }
    }

    // ==================================================================
    //  主窗体
    // ==================================================================
    public class SolarForm : Form
    {
        // ---- 输入控件 ----
        private ComboBox cmbCity;
        private NumericUpDown numLat, numLon, numTZ, numTilt, numAzim;
        private DateTimePicker dtpDate, dtpTime;
        private ComboBox cmbBase;

        // ---- 输出控件 ----
        private Label[] tileValues = new Label[12];
        private RichTextBox rtbReport;
        private SolarChartPanel chart;
        private Label lblStatus;
        private TabControl tabs;

        private SolarInput lastInput;
        private SolarResult lastResult;

        private bool loadingCities;

        // ---- 常用城市 ----
        private class City
        {
            public string Name; public double Lat; public double Lon; public double TZ;
            public City(string n, double la, double lo, double tz) { Name = n; Lat = la; Lon = lo; TZ = tz; }
            public override string ToString() { return Name; }
        }

        private static readonly City[] Cities = new City[]
        {
            new City("（自定义）",          double.NaN, double.NaN, 8.0),
            new City("上海  31.12°N",       31.12, 121.47, 8.0),
            new City("北京  39.90°N",       39.90, 116.41, 8.0),
            new City("广州  23.13°N",       23.13, 113.26, 8.0),
            new City("西安  34.27°N",       34.27, 108.95, 8.0),
            new City("哈尔滨 45.80°N",      45.80, 126.53, 8.0),
            new City("拉萨  29.65°N",       29.65,  91.14, 8.0),
            new City("乌鲁木齐 43.83°N",     43.83,  87.62, 8.0),
            new City("三亚  18.25°N",       18.25, 109.51, 8.0),
            new City("香港  22.32°N",       22.32, 114.17, 8.0),
            new City("台北  25.03°N",       25.03, 121.57, 8.0)
        };

        private static Font UiFont(float size)
        {
            string[] names = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun" };
            foreach (string n in names)
            {
                try
                {
                    Font f = new Font(n, size);
                    if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font(FontFamily.GenericSansSerif, size);
        }

        private static Font MonoFont(float size)
        {
            string[] names = new string[] { "Cascadia Mono", "Consolas", "Courier New", "NSimSun" };
            foreach (string n in names)
            {
                try
                {
                    Font f = new Font(n, size);
                    if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font(FontFamily.GenericMonospace, size);
        }

        public SolarForm()
        {
            Font uiFont = UiFont(9f);
            this.Font = uiFont;
            this.Text = "太阳能光伏计算器 —— 太阳位置与太阳入射角";
            this.ClientSize = new Size(1220, 780);
            this.MinimumSize = new Size(1000, 680);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AutoScaleMode = AutoScaleMode.Font;
            this.KeyPreview = true;
            this.KeyDown += new KeyEventHandler(Form_KeyDown);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
            root.Padding = new Padding(8, 8, 8, 0);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 2;
            content.RowCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 372f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            content.Controls.Add(BuildInputGroup(uiFont), 0, 0);
            content.Controls.Add(BuildOutputTabs(uiFont), 1, 0);

            root.Controls.Add(content, 0, 0);
            root.Controls.Add(BuildButtonBar(uiFont), 0, 1);
            this.Controls.Add(root);

            this.Load += new EventHandler(Form_Load);
            // 窗体首次显示后把计算书滚动回顶部（Load 阶段控件尚未完成布局，滚动会失效）
            this.Shown += new EventHandler(delegate(object s, EventArgs e) { ScrollReportToTop(); });
        }

        // ---------------- 左侧输入区 ----------------
        private GroupBox BuildInputGroup(Font uiFont)
        {
            GroupBox box = new GroupBox();
            box.Text = "  输入参数  ";
            box.Dock = DockStyle.Fill;
            box.Padding = new Padding(10, 6, 10, 10);
            box.Font = uiFont;

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.AutoScroll = true;
            box.Controls.Add(t);

            int row = 0;

            // 常用城市
            cmbCity = new ComboBox();
            cmbCity.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbCity.Dock = DockStyle.Fill;
            cmbCity.Items.AddRange(Cities);
            cmbCity.SelectedIndex = 1;   // 上海
            cmbCity.SelectedIndexChanged += new EventHandler(CmbCity_Changed);
            AddRow(t, ref row, "常用城市", cmbCity);
            AddSep(t, ref row);

            // 纬度
            numLat = NewNum(-90m, 90m, 4, 0.01m, 31.12m);
            AddRow(t, ref row, "纬度 φ (°)", numLat);
            AddHint(t, ref row, "北纬为正（+），南纬为负（−）");

            // 经度
            numLon = NewNum(-180m, 180m, 4, 0.01m, 121.47m);
            AddRow(t, ref row, "经度 λ (°)", numLon);
            AddHint(t, ref row, "东经为正（+），西经为负（−）");

            // 时区
            numTZ = NewNum(-12m, 14m, 1, 0.5m, 8m);
            AddRow(t, ref row, "时区 (UTC±h)", numTZ);
            AddHint(t, ref row, "北京时间 = UTC+8");

            // 日期
            dtpDate = new DateTimePicker();
            dtpDate.Format = DateTimePickerFormat.Short;
            dtpDate.Dock = DockStyle.Fill;
            dtpDate.Value = DateTime.Today;
            dtpDate.ValueChanged += new EventHandler(AnyInput_Changed);
            AddRow(t, ref row, "日期", dtpDate);
            AddHint(t, ref row, "序日 n 由日期自动确定");

            // 时刻
            dtpTime = new DateTimePicker();
            dtpTime.Format = DateTimePickerFormat.Custom;
            dtpTime.CustomFormat = "HH:mm:ss";
            dtpTime.ShowUpDown = true;
            dtpTime.Dock = DockStyle.Fill;
            dtpTime.Value = new DateTime(2000, 1, 1, 14, 0, 0);
            dtpTime.ValueChanged += new EventHandler(AnyInput_Changed);
            AddRow(t, ref row, "时刻", dtpTime);

            // 时刻基准
            cmbBase = new ComboBox();
            cmbBase.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbBase.Dock = DockStyle.Fill;
            cmbBase.Items.AddRange(new object[]
            {
                "真太阳时（教材基准）",
                "地方平太阳时",
                "标准时（北京时间）"
            });
            cmbBase.SelectedIndex = 0;
            AddRow(t, ref row, "时刻基准", cmbBase);
            AddHint(t, ref row, "以真太阳时为基准");
            AddSep(t, ref row);

            // 倾角
            numTilt = NewNum(0m, 180m, 2, 1m, 31.12m);
            AddRow(t, ref row, "光伏板倾角 β (°)", numTilt);
            AddHint(t, ref row, "0° = 水平，90° = 垂直，180° = 倒置");

            // 方位角
            numAzim = NewNum(-180m, 180m, 2, 5m, 0m);
            AddRow(t, ref row, "光伏板方位角 γ (°)", numAzim);
            AddHint(t, ref row, "0° = 正南，西为正，东为负");
            AddSep(t, ref row);

            Label tip = new Label();
            tip.Text = "提示：点击左下［计 算 / F5］即可得到全部结果；"
                     + "结果页含逐式代入过程，可复制或保存为 txt。";
            tip.AutoSize = false;
            tip.Dock = DockStyle.Fill;
            tip.Height = 54;
            tip.ForeColor = Color.FromArgb(110, 120, 135);
            t.Controls.Add(tip, 0, row);
            t.SetColumnSpan(tip, 2);
            row++;

            return box;
        }

        private NumericUpDown NewNum(decimal min, decimal max, int dp, decimal inc, decimal val)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.DecimalPlaces = dp;
            n.Increment = inc;
            n.Value = val;
            n.Dock = DockStyle.Fill;
            n.TextAlign = HorizontalAlignment.Right;
            n.ValueChanged += new EventHandler(AnyInput_Changed);
            return n;
        }

        private void AddRow(TableLayoutPanel t, ref int row, string caption, Control c)
        {
            Label l = new Label();
            l.Text = caption;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(3, 4, 3, 4);
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            t.Controls.Add(l, 0, row);
            c.Margin = new Padding(3, 4, 3, 4);
            t.Controls.Add(c, 1, row);
            row++;
        }

        private void AddHint(TableLayoutPanel t, ref int row, string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Dock = DockStyle.Fill;
            l.ForeColor = Color.FromArgb(130, 140, 155);
            l.Font = new Font(this.Font.FontFamily, 8f);
            l.Margin = new Padding(6, 0, 3, 4);
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
            t.Controls.Add(l, 1, row);
            row++;
        }

        private void AddSep(TableLayoutPanel t, ref int row)
        {
            Label l = new Label();
            l.BorderStyle = BorderStyle.Fixed3D;
            l.Height = 2;
            l.Dock = DockStyle.Fill;
            l.Margin = new Padding(3, 6, 3, 8);
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 16f));
            t.Controls.Add(l, 0, row);
            t.SetColumnSpan(l, 2);
            row++;
        }

        // ---------------- 右侧输出区 ----------------
        private TabControl BuildOutputTabs(Font uiFont)
        {
            TabControl tabsCtl = new TabControl();
            tabsCtl.Dock = DockStyle.Fill;
            tabsCtl.Font = uiFont;
            tabs = tabsCtl;

            // --- 结果页 ---
            TabPage pageResult = new TabPage("计算结果");
            pageResult.Padding = new Padding(4);
            TableLayoutPanel res = new TableLayoutPanel();
            res.Dock = DockStyle.Fill;
            res.ColumnCount = 1;
            res.RowCount = 2;
            res.RowStyles.Add(new RowStyle(SizeType.Absolute, 168f));
            res.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            TableLayoutPanel tiles = new TableLayoutPanel();
            tiles.Dock = DockStyle.Fill;
            tiles.ColumnCount = 4;
            tiles.RowCount = 3;
            for (int i = 0; i < 4; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            for (int i = 0; i < 3; i++) tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));

            string[] caps = new string[]
            {
                "太阳赤纬角 δ", "时角 τ", "太阳高度角 h", "太阳方位角 γs",
                "太阳天顶角 θz", "正午太阳高度角", "日照时间 T", "太阳入射角 θ",
                "日出时角 τ0出", "日没时角 τ0没", "日出时刻", "日没时刻"
            };
            int idx = 0;
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    Label v;
                    Panel tile = MakeTile(caps[idx], uiFont, out v);
                    tileValues[idx] = v;
                    tiles.Controls.Add(tile, c, r);
                    idx++;
                }
            }

            rtbReport = new RichTextBox();
            rtbReport.Dock = DockStyle.Fill;
            rtbReport.ReadOnly = true;
            rtbReport.WordWrap = false;
            rtbReport.ScrollBars = RichTextBoxScrollBars.Both;
            rtbReport.BackColor = Color.White;
            rtbReport.Font = MonoFont(9.5f);
            rtbReport.Text = "请点击左下角［计 算］按钮。";

            res.Controls.Add(tiles, 0, 0);
            res.Controls.Add(rtbReport, 0, 1);
            pageResult.Controls.Add(res);

            // --- 曲线页 ---
            TabPage pageChart = new TabPage("日照曲线");
            pageChart.Padding = new Padding(4);
            chart = new SolarChartPanel();
            chart.Dock = DockStyle.Fill;
            chart.Font = uiFont;
            pageChart.Controls.Add(chart);

            // --- 公式页 ---
            TabPage pageHelp = new TabPage("公式说明");
            pageHelp.Padding = new Padding(4);
            RichTextBox help = new RichTextBox();
            help.Dock = DockStyle.Fill;
            help.ReadOnly = true;
            help.WordWrap = false;
            help.BackColor = Color.White;
            help.Font = MonoFont(9.5f);
            help.Text = HelpText();
            pageHelp.Controls.Add(help);

            tabs.TabPages.Add(pageResult);
            tabs.TabPages.Add(pageChart);
            tabs.TabPages.Add(pageHelp);
            return tabs;
        }

        private Panel MakeTile(string caption, Font uiFont, out Label value)
        {
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.ColumnCount = 1;
            p.RowCount = 2;
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            p.Margin = new Padding(3);
            p.BackColor = Color.FromArgb(244, 248, 253);
            p.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;

            Label cap = new Label();
            cap.Text = caption;
            cap.Dock = DockStyle.Fill;
            cap.ForeColor = Color.FromArgb(95, 105, 120);
            cap.Font = new Font(uiFont.FontFamily, 8.5f);
            cap.TextAlign = ContentAlignment.MiddleLeft;
            cap.Padding = new Padding(6, 0, 0, 0);

            value = new Label();
            value.Text = "—";
            value.Dock = DockStyle.Fill;
            value.ForeColor = Color.FromArgb(18, 62, 130);
            value.Font = new Font(uiFont.FontFamily, 12.5f, FontStyle.Bold);
            value.TextAlign = ContentAlignment.MiddleLeft;
            value.Padding = new Padding(6, 0, 0, 2);

            p.Controls.Add(cap, 0, 0);
            p.Controls.Add(value, 0, 1);
            return p;
        }

        private void ScrollReportToTop()
        {
            if (rtbReport == null || rtbReport.TextLength == 0) return;
            rtbReport.SelectionStart = 0;
            rtbReport.SelectionLength = 0;
            rtbReport.ScrollToCaret();
        }

        private Panel BuildButtonBar(Font uiFont)
        {
            TableLayoutPanel bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.ColumnCount = 2;
            bar.RowCount = 1;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.Padding = new Padding(0, 6, 0, 6);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = false;

            Button btnCalc = NewButton("计 算 (F5)", 118);
            btnCalc.Font = new Font(uiFont.FontFamily, 9.5f, FontStyle.Bold);
            btnCalc.Click += new EventHandler(BtnCalc_Click);

            Button btnCopy = NewButton("复制结果", 90);
            btnCopy.Click += new EventHandler(BtnCopy_Click);

            Button btnSave = NewButton("保存为 txt", 100);
            btnSave.Click += new EventHandler(BtnSave_Click);

            Button btnReset = NewButton("恢复默认", 90);
            btnReset.Click += new EventHandler(BtnReset_Click);

            flow.Controls.Add(btnCalc);
            flow.Controls.Add(btnCopy);
            flow.Controls.Add(btnSave);
            flow.Controls.Add(btnReset);

            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.ForeColor = Color.FromArgb(95, 105, 120);
            lblStatus.Text = "就绪";

            bar.Controls.Add(flow, 0, 0);
            bar.Controls.Add(lblStatus, 1, 0);
            return bar;
        }

        private Button NewButton(string text, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = w;
            b.Height = 32;
            b.Margin = new Padding(0, 0, 8, 0);
            b.UseVisualStyleBackColor = true;
            b.Font = new Font(this.Font.FontFamily, 9f);
            return b;
        }

        // ---------------- 事件 ----------------
        private void Form_Load(object sender, EventArgs e)
        {
            Calc();
        }

        private void Form_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                Calc();
                e.Handled = true;
            }
        }

        private void CmbCity_Changed(object sender, EventArgs e)
        {
            if (loadingCities) return;
            City c = cmbCity.SelectedItem as City;
            if (c == null || double.IsNaN(c.Lat)) return;
            loadingCities = true;
            numLat.Value = (decimal)c.Lat;
            numLon.Value = (decimal)c.Lon;
            numTZ.Value = (decimal)c.TZ;
            loadingCities = false;
            Calc();
        }

        private void AnyInput_Changed(object sender, EventArgs e)
        {
            if (loadingCities) return;
        }

        private void BtnCalc_Click(object sender, EventArgs e) { Calc(); }

        private void BtnCopy_Click(object sender, EventArgs e)
        {
            if (rtbReport.TextLength == 0) return;
            try
            {
                Clipboard.SetText(rtbReport.Text);
                lblStatus.Text = "结果已复制到剪贴板。";
            }
            catch (Exception ex)
            {
                MessageBox.Show("复制失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (rtbReport.TextLength == 0) return;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                dlg.FileName = string.Format("太阳能计算结果_{0:yyyyMMdd_HHmm}.txt", DateTime.Now);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true)))
                        {
                            sw.Write(rtbReport.Text);
                        }
                        lblStatus.Text = "已保存：" + dlg.FileName;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            loadingCities = true;
            cmbCity.SelectedIndex = 1;
            numLat.Value = 31.12m;
            numLon.Value = 121.47m;
            numTZ.Value = 8m;
            dtpDate.Value = DateTime.Today;
            dtpTime.Value = new DateTime(2000, 1, 1, 14, 0, 0);
            cmbBase.SelectedIndex = 0;
            numTilt.Value = 31.12m;
            numAzim.Value = 0m;
            loadingCities = false;
            Calc();
        }

        // ---------------- 计算 ----------------
        private void Calc()
        {
            try
            {
                SolarInput inp = new SolarInput();
                inp.Latitude = (double)numLat.Value;
                inp.Longitude = (double)numLon.Value;
                inp.TimeZone = (double)numTZ.Value;
                inp.Year = dtpDate.Value.Year;
                inp.Month = dtpDate.Value.Month;
                inp.Day = dtpDate.Value.Day;
                inp.ClockHour = dtpTime.Value.Hour + dtpTime.Value.Minute / 60.0 + dtpTime.Value.Second / 3600.0;
                inp.TimeBase = (TimeBase)cmbBase.SelectedIndex;
                inp.Tilt = (double)numTilt.Value;
                inp.Azimuth = (double)numAzim.Value;

                SolarResult r = SolarMath.Compute(inp);
                lastInput = inp;
                lastResult = r;

                // 速览磁贴
                tileValues[0].Text = string.Format("{0:F2}°", r.Declination);
                tileValues[1].Text = string.Format("{0:F2}°", r.HourAngle);
                tileValues[2].Text = string.Format("{0:F2}°", r.Altitude);
                tileValues[3].Text = string.Format("{0:F2}°", r.Azimuth);
                tileValues[4].Text = string.Format("{0:F2}°", r.Zenith);
                tileValues[5].Text = string.Format("{0:F2}°", r.NoonAltitude);
                tileValues[6].Text = string.Format("{0:F2} h", r.DayLength);
                tileValues[7].Text = string.Format("{0:F2}°", r.Incidence);
                tileValues[8].Text = string.Format("{0:F2}°", r.SunriseHourAngle);
                tileValues[9].Text = string.Format("{0:F2}°", r.SunsetHourAngle);
                tileValues[10].Text = SolarMath.FormatHourShort(r.SunriseClockTime);
                tileValues[11].Text = SolarMath.FormatHourShort(r.SunsetClockTime);

                rtbReport.Text = SolarMath.BuildReport(inp, r);
                ScrollReportToTop();

                chart.SetData(inp, r);

                lblStatus.Text = string.Format("{0:yyyy-MM-dd} {1}   φ={2:F2}°  β={3:F2}°  γ={4:F2}°"
                    + "   →   δ={5:F2}°  τ={6:F2}°  h={7:F2}°  γs={8:F2}°  T={9:F2} h  θ={10:F2}°",
                    r.Date, SolarMath.FormatHour(inp.ClockHour), inp.Latitude, inp.Tilt, inp.Azimuth,
                    r.Declination, r.HourAngle, r.Altitude, r.Azimuth, r.DayLength, r.Incidence);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "输入有误：" + ex.Message;
                MessageBox.Show(ex.Message, "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public SolarInput LastInput { get { return lastInput; } }
        public SolarResult LastResult { get { return lastResult; } }

        /// <summary>切换到指定标签页（0=计算结果，1=日照曲线，2=公式说明）。</summary>
        public void SelectTab(int index)
        {
            if (tabs != null && index >= 0 && index < tabs.TabPages.Count)
                tabs.SelectedIndex = index;
        }

        /// <summary>把一组输入条件写入界面并重新计算（供脚本/批处理调用）。</summary>
        public void ApplyInputs(SolarInput inp)
        {
            if (inp == null) return;
            loadingCities = true;
            numLat.Value = ClampDec((decimal)inp.Latitude, numLat.Minimum, numLat.Maximum);
            numLon.Value = ClampDec((decimal)inp.Longitude, numLon.Minimum, numLon.Maximum);
            numTZ.Value = ClampDec((decimal)inp.TimeZone, numTZ.Minimum, numTZ.Maximum);
            dtpDate.Value = new DateTime(inp.Year, inp.Month, inp.Day);
            int hh = (int)Math.Floor(inp.ClockHour);
            int mm = (int)Math.Floor((inp.ClockHour - hh) * 60.0);
            int ss = (int)Math.Round((((inp.ClockHour - hh) * 60.0) - mm) * 60.0);
            if (ss > 59) ss = 59;
            if (hh > 23) hh = 23;
            dtpTime.Value = new DateTime(2000, 1, 1, hh, mm, ss);
            cmbBase.SelectedIndex = (int)inp.TimeBase;
            numTilt.Value = ClampDec((decimal)inp.Tilt, numTilt.Minimum, numTilt.Maximum);
            numAzim.Value = ClampDec((decimal)inp.Azimuth, numAzim.Minimum, numAzim.Maximum);
            loadingCities = false;
            Calc();
        }

        private static decimal ClampDec(decimal v, decimal lo, decimal hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static string HelpText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("太阳能光伏 —— 太阳位置与太阳入射角 计算公式一览");
            sb.AppendLine("（依据教材《2.2 太阳与地球》，全部角度单位均为度(°)）");
            sb.AppendLine(new string('=', 78));
            sb.AppendLine();
            sb.AppendLine("符号说明");
            sb.AppendLine("  φ   观测点地理纬度（北纬为正）");
            sb.AppendLine("  δ   太阳赤纬角");
            sb.AppendLine("  τ   太阳时角（正午为 0，下午为正，上午为负）");
            sb.AppendLine("  h   太阳高度角（太阳光线与地平面的夹角）");
            sb.AppendLine("  θz  太阳天顶角，θz = 90° − h");
            sb.AppendLine("  γs  太阳方位角（自正南起算，向西为正、向东为负，范围 ±180°）");
            sb.AppendLine("  n   序日（1月1日为 1）");
            sb.AppendLine("  β   光伏板倾角（0° 为水平）");
            sb.AppendLine("  γ   光伏板方位角（0° 为正南，向西为正）");
            sb.AppendLine("  θ   太阳入射角（太阳光线与光伏板法线的夹角）");
            sb.AppendLine();
            sb.AppendLine(new string('-', 78));
            sb.AppendLine();
            sb.AppendLine("1. 太阳赤纬角 δ");
            sb.AppendLine("     δ = 23.45° · sin[ 360° × (284 + n) / 365 ]");
            sb.AppendLine("   例：9月22日 n = 265，δ = −0.6°");
            sb.AppendLine();
            sb.AppendLine("2. 时角 τ");
            sb.AppendLine("     τ = 15° × ( 真太阳时 − 12 )      （每小时 15°，下午为正）");
            sb.AppendLine("   例：下午 2 时（14:00）→ τ = 15° × 2 = 30°");
            sb.AppendLine();
            sb.AppendLine("3. 太阳高度角 h                                    式 (2-3)");
            sb.AppendLine("     sin h = sinφ · sinδ + cosφ · cosδ · cosτ");
            sb.AppendLine("   正午 τ = 0 时简化为： sin h = cos(φ − δ)  →  h = 90° − |φ − δ|");
            sb.AppendLine();
            sb.AppendLine("4. 太阳方位角 γs                              式 (2-7)、(2-8)");
            sb.AppendLine("     cos γs = ( sin h · sinφ − sinδ ) / ( cos h · cosφ )        (2-7)");
            sb.AppendLine("     sin γs = cosδ · sinτ / cos h                              (2-8)");
            sb.AppendLine("   本程序用两式的分子作 atan2 运算，与上面两式数学上完全等价，");
            sb.AppendLine("   但在太阳接近天顶（cos h → 0）时数值更稳定，且自动得到正确象限。");
            sb.AppendLine();
            sb.AppendLine("5. 日出、日没时角 τ0                               式 (2-9)");
            sb.AppendLine("     cos τ0 = − tanφ · tanδ");
            sb.AppendLine("     τ0出 = −arccos(−tanφ tanδ)      τ0没 = +arccos(−tanφ tanδ)");
            sb.AppendLine("   |值| ≥ 1 时为极昼或极夜（程序自动判别）。");
            sb.AppendLine();
            sb.AppendLine("6. 日照时间 T（昼长）                             式 (1-11)");
            sb.AppendLine("     T = (2 / 15°) · arccos( − tanφ · tanδ ) = 2·|τ0| / 15°");
            sb.AppendLine("     日出时刻 = 12 − T/2      日没时刻 = 12 + T/2");
            sb.AppendLine("   例：上海冬至日 T = 2 × 74.82° / 15° = 9.98 h");
            sb.AppendLine();
            sb.AppendLine("7. 太阳入射角 θ");
            sb.AppendLine("     cos θ = sinδ ( sinφ cosβ − cosφ sinβ cosγ )");
            sb.AppendLine("           + cosδ cosτ ( cosφ cosβ + sinφ sinβ cosγ )");
            sb.AppendLine("           + cosδ sinβ sinγ sinτ");
            sb.AppendLine("   当 β = 0（水平面）时 θ = θz；当板面正对太阳时 θ = 0。");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine(new string('-', 78));
            sb.AppendLine();
            sb.AppendLine("时刻基准换算（当输入为地方平太阳时或标准时时使用）");
            sb.AppendLine("   经度修正 = 4 min/° × ( λ − 15°/h × 时区 )");
            sb.AppendLine("   均时差   EoT = 9.87 sin(2B) − 7.53 cos B − 1.5 sin B ，");
            sb.AppendLine("                  B = 360° × (n − 81) / 364   （单位：min）");
            sb.AppendLine("   真太阳时 = 输入时刻 + ( 经度修正 + EoT ) / 60");
            sb.AppendLine();
            sb.AppendLine("说明：教材例题均以真太阳时为基准，故默认选择［真太阳时］时，");
            return sb.ToString();
        }
    }

    // ==================================================================
    //  程序入口
    // ==================================================================
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SolarForm());
        }
    }
}
