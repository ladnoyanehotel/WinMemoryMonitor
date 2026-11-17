using System;
using System.Windows;
using System.Windows.Media;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Lightweight sparkline that renders the last 60 samples of a <see cref="MetricHistory"/>
    /// with cached pens and brushes. The control redraws only when the host invalidates it
    /// (about once per second); no objects are created per data point.
    /// </summary>
    public class SparklineControl : FrameworkElement
    {
        #region Fields

        private const float Maximum = 100f;

        private readonly float[] _buffer = new float[64];
        private readonly Pen _linePen;
        private readonly Pen _baselinePen;
        private readonly Brush _fillBrush;

        #endregion

        #region Dependency properties

        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(MetricHistory), typeof(SparklineControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Gets or sets the metric history to render
        /// </summary>
        public MetricHistory Source
        {
            get { return (MetricHistory)GetValue(SourceProperty); }
            set { SetValue(SourceProperty, value); }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SparklineControl"/> class.
        /// </summary>
        public SparklineControl()
        {
            var color = (Color)ColorConverter.ConvertFromString("#00AEF7");

            _linePen = new Pen(new SolidColorBrush(color), 1.2);
            _baselinePen = new Pen(new SolidColorBrush(Color.FromRgb(60, 60, 60)), 1.0);
            _fillBrush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B));

            _linePen.Freeze();
            _baselinePen.Freeze();
            _fillBrush.Freeze();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Renders the sparkline from the ring buffer
        /// </summary>
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var width = ActualWidth;
            var height = ActualHeight;

            if (width <= 0 || height <= 0)
                return;

            // Baseline
            drawingContext.DrawLine(_baselinePen, new Point(0, height - 1), new Point(width, height - 1));

            var source = Source;

            if (source == null || source.Count < 2)
                return;

            var count = source.CopyTo(_buffer, 0);

            if (count < 2)
                return;

            var stepX = width / (count - 1);
            var points = new Point[count];

            for (var i = 0; i < count; i++)
            {
                var value = _buffer[i];

                if (value < 0)
                    value = 0;

                if (value > Maximum)
                    value = Maximum;

                points[i] = new Point(i * stepX, height - 2 - (height - 4) * (value / Maximum));
            }

            // Filled area under the line
            var geometry = new StreamGeometry();
            StreamGeometryContext context;

            using (context = geometry.Open())
            {
                context.BeginFigure(points[0], true, true);

                for (var i = 1; i < count; i++)
                    context.LineTo(points[i], true, false);

                context.LineTo(new Point(points[count - 1].X, height - 1), true, false);
                context.LineTo(new Point(points[0].X, height - 1), true, false);
            }

            geometry.Freeze();

            drawingContext.DrawGeometry(_fillBrush, null, geometry);

            for (var i = 1; i < count; i++)
                drawingContext.DrawLine(_linePen, points[i - 1], points[i]);
        }

        #endregion
    }
}
