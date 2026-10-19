using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;


namespace remeLog.Views.Controls
{
    public class InsertionAdorner : Adorner
    {
        private readonly bool _isAbove;
        private readonly Pen _pen;

        public InsertionAdorner(UIElement adornedElement, bool isAbove)
            : base(adornedElement)
        {
            _isAbove = isAbove;
            _pen = new Pen(Brushes.Black, 1);
            _pen.Freeze();

            // Принудительно делаем адорнер видимым
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(AdornedElement.RenderSize);

            if (rect.Width == 0 || rect.Height == 0)
            {
                rect = new Rect(0, 0, 200, 20);
            }

            double y = _isAbove ? 0 : rect.Height;
            var startPoint = new Point(0, y);

            var triangleSize = 5;
            var leftTriangle = new Point[]
            {
            new Point(startPoint.X - triangleSize, y - triangleSize),
            new Point(startPoint.X - triangleSize, y + triangleSize),
            new Point(startPoint.X, y)
            };

            drawingContext.DrawGeometry(Brushes.Gray, null,
                new PathGeometry(new[] { new PathFigure(leftTriangle[0],
                new[] { new LineSegment(leftTriangle[1], true),
                        new LineSegment(leftTriangle[2], true) }, true) }));
        }
    }
}
