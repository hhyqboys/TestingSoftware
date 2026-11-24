// <copyright file=GaugeControl company="Sumarte Technology">
// Copyright (c) 2026 All Rights Reserved
// </copyright>
// <summary>轻量仪表控件：240° 刻度盘 + 指针 + 数字值，支持按量程比例做“绿-黄-红”渐变着色。</summary>
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TestingSoftware.Controls
{
    /// <summary>
    /// 仪表盘控件（无第三方依赖，直接 OnRender 绘制）。
    /// 量程从 <see cref="Minimum"/> 到 <see cref="Maximum"/>，指针按占比偏转。
    /// </summary>
    public class GaugeControl : FrameworkElement
    {
        /// <summary>量表下方起始角度（顺时针，单位为度，0° 为 +X 方向）。</summary>
        private const double StartAngle = 150d;

        /// <summary>量表总扫掠角度。</summary>
        private const double SweepAngle = 240d;

        /// <summary>刻度弧线宽度。</summary>
        private const double ArcThickness = 8d;

        #region 依赖属性

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(double), typeof(GaugeControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(GaugeControl),
            new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(GaugeControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(GaugeControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
            nameof(Caption), typeof(string), typeof(GaugeControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
            nameof(ValueFormat), typeof(string), typeof(GaugeControl),
            new FrameworkPropertyMetadata("F0", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty HeatModeProperty = DependencyProperty.Register(
            nameof(HeatMode), typeof(bool), typeof(GaugeControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
            nameof(Accent), typeof(Brush), typeof(GaugeControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xD0)), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
            nameof(Track), typeof(Brush), typeof(GaugeControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE4, 0xE9, 0xF0)), FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>量程下限。</summary>
        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        /// <summary>量程上限（指针满偏位置）。</summary>
        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        /// <summary>当前值。</summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>单位（显示在数值右侧）。</summary>
        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        /// <summary>标题（显示在表盘上方）。</summary>
        public string Caption
        {
            get => (string)GetValue(CaptionProperty);
            set => SetValue(CaptionProperty, value);
        }

        /// <summary>数值格式。</summary>
        public string ValueFormat
        {
            get => (string)GetValue(ValueFormatProperty);
            set => SetValue(ValueFormatProperty, value);
        }

        /// <summary>是否按占比做绿→黄→红着色（温度表用）。</summary>
        public bool HeatMode
        {
            get => (bool)GetValue(HeatModeProperty);
            set => SetValue(HeatModeProperty, value);
        }

        /// <summary>主题色（正常模式下的弧线与指针颜色）。</summary>
        public Brush Accent
        {
            get => (Brush)GetValue(AccentProperty);
            set => SetValue(AccentProperty, value);
        }

        /// <summary>刻度底弧颜色。</summary>
        public Brush Track
        {
            get => (Brush)GetValue(TrackProperty);
            set => SetValue(TrackProperty, value);
        }

        #endregion

        /// <summary>
        /// 绘制仪表盘。
        /// </summary>
        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 20 || height < 20) return;

            double captionHeight = Caption.Length > 0 ? 20 : 0;
            double pad = 10;

            // 240° 表盘的竖直投影为 1.5×半径，据此反算半径
            double radius = Math.Min((width - pad * 2) / 2, (height - captionHeight - pad - 16) / 1.5);
            if (radius <= 6) return;

            double cx = width / 2;
            double cy = captionHeight + pad + radius;

            double ratio = 0d;
            double range = Maximum - Minimum;
            if (range > 0)
            {
                ratio = (Value - Minimum) / range;
            }
            ratio = Math.Max(0d, Math.Min(1d, ratio));

            Brush valueBrush = HeatMode ? new SolidColorBrush(HeatColor(ratio)) : Accent;

            // 标题
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (captionHeight > 0)
            {
                var captionText = MakeText(Caption, 12, new SolidColorBrush(Color.FromRgb(0x5A, 0x66, 0x77)), dpi);
                dc.DrawText(captionText, new Point(cx - captionText.Width / 2, 2));
            }

            // 底弧
            DrawArc(dc, cx, cy, radius, StartAngle, StartAngle + SweepAngle, Track, ArcThickness);

            // 数值弧（指针偏转越大，着色弧越长）
            if (ratio > 0.001)
            {
                DrawArc(dc, cx, cy, radius, StartAngle, StartAngle + SweepAngle * ratio, valueBrush, ArcThickness);
            }

            // 刻度与量程端点标签
            DrawTicks(dc, cx, cy, radius, dpi, valueBrush);

            // 指针
            DrawNeedle(dc, cx, cy, radius, StartAngle + SweepAngle * ratio, valueBrush);

            // 中心支点
            dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xD2, 0xDF)), 1), new Point(cx, cy), 7, 7);

            // 数值 + 单位
            Brush textBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x25, 0x33));
            var valueText = MakeText(Value.ToString(ValueFormat, CultureInfo.CurrentCulture), Math.Max(18, radius * 0.46), textBrush, dpi);
            var unitText = Unit.Length > 0 ? MakeText(Unit, Math.Max(11, radius * 0.17), new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x88)), dpi) : null;

            double valueY = cy + radius * 0.36;
            if (unitText != null)
            {
                double totalWidth = valueText.Width + 4 + unitText.Width;
                double left = cx - totalWidth / 2;
                dc.DrawText(valueText, new Point(left, valueY - valueText.Height / 2));
                dc.DrawText(unitText, new Point(left + valueText.Width + 4, valueY + valueText.Height / 2 - unitText.Height - 2));
            }
            else
            {
                dc.DrawText(valueText, new Point(cx - valueText.Width / 2, valueY - valueText.Height / 2));
            }
        }

        /// <summary>
        /// 用折线逼近圆弧并描边。
        /// </summary>
        private static void DrawArc(DrawingContext dc, double cx, double cy, double radius, double fromDeg, double toDeg, Brush brush, double thickness)
        {
            if (Math.Abs(toDeg - fromDeg) < 0.5) return;

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                bool first = true;
                for (double deg = fromDeg; deg <= toDeg; deg += 1.5)
                {
                    Point p = Polar(cx, cy, radius, deg);
                    if (first)
                    {
                        ctx.BeginFigure(p, false, false);
                        first = false;
                    }
                    else
                    {
                        ctx.LineTo(p, true, false);
                    }
                }

                // 补齐终点，避免最后一小段缺失
                Point end = Polar(cx, cy, radius, toDeg);
                if (!first) ctx.LineTo(end, true, false);
            }
            geometry.Freeze();

            Pen pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        /// <summary>
        /// 绘制刻度线与量程端点数值。
        /// </summary>
        private void DrawTicks(DrawingContext dc, double cx, double cy, double radius, double dpi, Brush valueBrush)
        {
            Pen tickPen = new Pen(new SolidColorBrush(Color.FromRgb(0x9F, 0xAC, 0xBD)), 1);
            tickPen.Freeze();

            const int tickCount = 6;
            for (int i = 0; i <= tickCount; i++)
            {
                double deg = StartAngle + SweepAngle * i / tickCount;
                Point inner = Polar(cx, cy, radius - ArcThickness / 2 - 1, deg);
                Point outer = Polar(cx, cy, radius + ArcThickness / 2 + 1, deg);
                dc.DrawLine(tickPen, inner, outer);
            }

            // 量程两端标签
            Brush labelBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xA6));
            var minText = MakeText(Minimum.ToString("F0", CultureInfo.CurrentCulture), 11, labelBrush, dpi);
            var maxText = MakeText(Maximum.ToString("F0", CultureInfo.CurrentCulture), 11, new SolidColorBrush(Color.FromRgb(0x4A, 0x57, 0x68)), dpi);

            Point minPos = Polar(cx, cy, radius + ArcThickness / 2 + 14, StartAngle);
            Point maxPos = Polar(cx, cy, radius + ArcThickness / 2 + 14, StartAngle + SweepAngle);

            dc.DrawText(minText, new Point(minPos.X - minText.Width / 2, minPos.Y - minText.Height / 2));
            dc.DrawText(maxText, new Point(maxPos.X - maxText.Width / 2, maxPos.Y - maxText.Height / 2));
        }

        /// <summary>
        /// 绘制指针（带锥度的箭头形）。
        /// </summary>
        private static void DrawNeedle(DrawingContext dc, double cx, double cy, double radius, double deg, Brush brush)
        {
            double rad = deg * Math.PI / 180d;
            double tipRadius = radius - ArcThickness - 6;

            Point tip = new Point(cx + tipRadius * Math.Cos(rad), cy + tipRadius * Math.Sin(rad));
            Point dir = new Point(Math.Cos(rad), Math.Sin(rad));
            Point normal = new Point(-dir.Y, dir.X);

            const double halfBase = 5d;
            Point baseLeft = new Point(cx + normal.X * halfBase, cy + normal.Y * halfBase);
            Point baseRight = new Point(cx - normal.X * halfBase, cy - normal.Y * halfBase);

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(baseLeft, true, false);
                ctx.LineTo(baseRight, true, false);
            }
            geometry.Freeze();

            dc.DrawGeometry(brush, null, geometry);
        }

        /// <summary>
        /// 极坐标 -> 屏幕坐标（角度顺时针、0° 指向 +X）。
        /// </summary>
        private static Point Polar(double cx, double cy, double radius, double deg)
        {
            double rad = deg * Math.PI / 180d;
            return new Point(cx + radius * Math.Cos(rad), cy + radius * Math.Sin(rad));
        }

        /// <summary>
        /// 按占比取“绿 → 黄 → 红”渐变色。
        /// </summary>
        private static Color HeatColor(double ratio)
        {
            ratio = Math.Max(0d, Math.Min(1d, ratio));

            Color green = Color.FromRgb(0x2F, 0xA8, 0x4F);
            Color amber = Color.FromRgb(0xEF, 0xB3, 0x1B);
            Color red = Color.FromRgb(0xE2, 0x3B, 0x2E);

            Color from = ratio < 0.5 ? green : amber;
            Color to = ratio < 0.5 ? amber : red;
            double t = ratio < 0.5 ? ratio / 0.5 : (ratio - 0.5) / 0.5;

            return Color.FromRgb(
                (byte)(from.R + (to.R - from.R) * t),
                (byte)(from.G + (to.G - from.G) * t),
                (byte)(from.B + (to.B - from.B) * t));
        }

        /// <summary>
        /// 生成 FormattedText。
        /// </summary>
        private FormattedText MakeText(string text, double size, Brush brush, double dpi)
        {
            return new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size,
                brush,
                dpi);
        }
    }
}
