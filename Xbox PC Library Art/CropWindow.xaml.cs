using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XboxSteamCoverArtFixer
{
    public partial class CropWindow : Window
    {
        private readonly byte[] _sourceBytes;
        private BitmapSource? _bmp;

        private double _baseScale;          // fit so min dimension covers crop square
        private double _zoom = 1.15;        // start slightly zoomed so we can pan
        private double _tx, _ty;            // translation in screen px
        private bool _dragging;
        private Point _lastPos;

        private Rect _cropRect;             // crop square in screen coords

        public byte[]? CroppedPng { get; private set; }

        public CropWindow(byte[] imageBytes, string fileLabel = "")
        {
            InitializeComponent();
            _sourceBytes = imageBytes;
            FileLabel.Text = fileLabel;
            Loaded += CropWindow_Loaded;
        }

        // ---------- init ----------
        private void CropWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                using var ms = new MemoryStream(_sourceBytes, writable: false);
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                _bmp = decoder.Frames[0];

                Img.Source = _bmp;
                Img.Width = _bmp.PixelWidth;
                Img.Height = _bmp.PixelHeight;

                RecomputeCropOverlay();
                ComputeBaseScale();
                EnsurePannableMargin();
                CenterImage();
                ApplyTransform();

                ImageHost.Focus(); // enable arrow keys immediately
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the selected image.\n\n{ex.Message}", "Crop", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void ImageHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            RecomputeCropOverlay();
            if (_bmp is null) return;

            ComputeBaseScale();
            EnsurePannableMargin();
            ConstrainTranslation();
            ApplyTransform();
        }

        // ---------- overlay / transforms ----------
        private void RecomputeCropOverlay()
        {
            var w = Math.Max(1.0, ImageHost.ActualWidth);
            var h = Math.Max(1.0, ImageHost.ActualHeight);
            var side = 0.8 * Math.Min(w, h);
            var x = (w - side) / 2.0;
            var y = (h - side) / 2.0;

            _cropRect = new Rect(x, y, side, side);

            Canvas.SetLeft(CropFrame, x); Canvas.SetTop(CropFrame, y);
            CropFrame.Width = side; CropFrame.Height = side;

            ShadeTop.Width = w; ShadeTop.Height = y; Canvas.SetLeft(ShadeTop, 0); Canvas.SetTop(ShadeTop, 0);
            ShadeBottom.Width = w; ShadeBottom.Height = h - (y + side); Canvas.SetLeft(ShadeBottom, 0); Canvas.SetTop(ShadeBottom, y + side);
            ShadeLeft.Width = x; ShadeLeft.Height = side; Canvas.SetLeft(ShadeLeft, 0); Canvas.SetTop(ShadeLeft, y);
            ShadeRight.Width = w - (x + side); ShadeRight.Height = side; Canvas.SetLeft(ShadeRight, x + side); Canvas.SetTop(ShadeRight, y);
        }

        private void ComputeBaseScale()
        {
            if (_bmp is null) return;
            double side = _cropRect.Width;
            double minDim = Math.Min(_bmp.PixelWidth, _bmp.PixelHeight);
            _baseScale = side / Math.Max(1.0, minDim);
        }

        // make sure there is slack to pan
        private void EnsurePannableMargin()
        {
            if (_bmp is null) return;
            double S = _baseScale * _zoom;
            double dispW = _bmp.PixelWidth * S;
            double dispH = _bmp.PixelHeight * S;

            if (dispW <= _cropRect.Width + 0.5 || dispH <= _cropRect.Height + 0.5)
            {
                _zoom *= 1.15;
                ZoomSlider.Value = _zoom;
            }
        }

        private void CenterImage()
        {
            if (_bmp is null) return;
            double S = _baseScale * _zoom;
            double dispW = _bmp.PixelWidth * S;
            double dispH = _bmp.PixelHeight * S;

            double cx = _cropRect.X + _cropRect.Width / 2.0;
            double cy = _cropRect.Y + _cropRect.Height / 2.0;

            _tx = cx - dispW / 2.0;
            _ty = cy - dispH / 2.0;

            ConstrainTranslation();
        }

        private void ApplyTransform()
        {
            if (_bmp is null) return;
            Img.RenderTransform = new TransformGroup
            {
                Children = new TransformCollection
                {
                    new ScaleTransform(_baseScale * _zoom, _baseScale * _zoom),
                    new TranslateTransform(_tx, _ty)
                }
            };
        }

        private void ConstrainTranslation()
        {
            if (_bmp is null) return;

            double S = _baseScale * _zoom;
            double dispW = _bmp.PixelWidth * S;
            double dispH = _bmp.PixelHeight * S;

            // keep image fully covering crop area
            _tx = Math.Min(_tx, _cropRect.X);
            _ty = Math.Min(_ty, _cropRect.Y);
            _tx = Math.Max(_tx, _cropRect.X + _cropRect.Width - dispW);
            _ty = Math.Max(_ty, _cropRect.Y + _cropRect.Height - dispH);
        }

        // ---------- panning / zooming ----------
        private void ImageHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                CenterOnScreenPoint(e.GetPosition(ImageHost));
                return;
            }

            _dragging = true;
            _lastPos = e.GetPosition(ImageHost);
            ImageHost.CaptureMouse();
            ImageHost.Cursor = Cursors.Hand;
        }

        private void ImageHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;

            var p = e.GetPosition(ImageHost);
            var dx = p.X - _lastPos.X;
            var dy = p.Y - _lastPos.Y;

            _tx += dx;
            _ty += dy;
            _lastPos = p;

            ConstrainTranslation();
            ApplyTransform();
        }

        private void ImageHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            ImageHost.ReleaseMouseCapture();
            ImageHost.Cursor = Cursors.Arrow;
        }

        private void ImageHost_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_bmp is null) return;

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // Horizontal pan with Shift + wheel
                Nudge(-(e.Delta / 3.0), 0);
                return;
            }

            // Zoom around crop center
            double oldZoom = _zoom;
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 0.9), 0.5, 6.0);

            double cx = _cropRect.X + _cropRect.Width / 2.0;
            double cy = _cropRect.Y + _cropRect.Height / 2.0;

            var S_old = _baseScale * oldZoom;
            var S_new = _baseScale * _zoom;

            double wx = (cx - _tx) / S_old;
            double wy = (cy - _ty) / S_old;

            _tx = cx - wx * S_new;
            _ty = cy - wy * S_new;

            EnsurePannableMargin();
            ConstrainTranslation();
            ApplyTransform();

            ZoomSlider.Value = _zoom;
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || _bmp is null) return;

            double oldZoom = _zoom;
            _zoom = ZoomSlider.Value;

            double cx = _cropRect.X + _cropRect.Width / 2.0;
            double cy = _cropRect.Y + _cropRect.Height / 2.0;

            var S_old = _baseScale * oldZoom;
            var S_new = _baseScale * _zoom;

            double wx = (cx - _tx) / S_old;
            double wy = (cy - _ty) / S_old;

            _tx = cx - wx * S_new;
            _ty = cy - wy * S_new;

            EnsurePannableMargin();
            ConstrainTranslation();
            ApplyTransform();
        }

        private void CenterOnScreenPoint(Point screenPt)
        {
            if (_bmp is null) return;

            double cx = _cropRect.X + _cropRect.Width / 2.0;
            double cy = _cropRect.Y + _cropRect.Height / 2.0;

            double S = _baseScale * _zoom;
            double wx = (screenPt.X - _tx) / S;
            double wy = (screenPt.Y - _ty) / S;

            _tx = cx - wx * S;
            _ty = cy - wy * S;

            ConstrainTranslation();
            ApplyTransform();
        }

        private void Nudge(double dx, double dy)
        {
            _tx += dx;
            _ty += dy;
            ConstrainTranslation();
            ApplyTransform();
        }

        // Arrow keys to nudge (Ctrl = larger step)
        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            double step = (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) ? 10 : 2;
            switch (e.Key)
            {
                case Key.Left: Nudge(-step, 0); e.Handled = true; break;
                case Key.Right: Nudge(+step, 0); e.Handled = true; break;
                case Key.Up: Nudge(0, -step); e.Handled = true; break;
                case Key.Down: Nudge(0, +step); e.Handled = true; break;
                case Key.Space: ImageHost.Cursor = Cursors.Hand; break; // visual hint
            }
        }
        private void Root_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && !_dragging)
                ImageHost.Cursor = Cursors.Arrow;
        }

        // ---------- buttons ----------
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            if (_bmp is null)
            {
                MessageBox.Show("Image not loaded.", "Crop", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double S = _baseScale * _zoom;
            int x = (int)Math.Floor((_cropRect.X - _tx) / S);
            int y = (int)Math.Floor((_cropRect.Y - _ty) / S);
            int w = (int)Math.Round(_cropRect.Width / S);
            int h = w;

            x = Math.Clamp(x, 0, Math.Max(0, _bmp.PixelWidth - 1));
            y = Math.Clamp(y, 0, Math.Max(0, _bmp.PixelHeight - 1));
            w = Math.Clamp(w, 1, _bmp.PixelWidth - x);
            h = Math.Clamp(h, 1, _bmp.PixelHeight - y);

            try
            {
                var cropped = new CroppedBitmap(_bmp, new Int32Rect(x, y, w, h));
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(cropped));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                CroppedPng = ms.ToArray();
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Crop", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
