using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Ui
{
    internal sealed class InletOptionsDialog : Window
    {
        private readonly ComboBox _typeCombo;
        private readonly TextBox _lengthBox;
        private readonly TextBox _depthBox;
        private readonly TextBox _slopeBox;
        private readonly TextBox _curbHeightBox;
        private readonly TextBlock _previewText;

        public InletCapacity.InletType SelectedType { get; private set; } = InletCapacity.InletType.GrateOnGrade;
        public double GrateLengthFt { get; private set; } = 5.0;
        public double FlowDepthFt { get; private set; } = 0.15;
        public double GutterSlope { get; private set; } = 0.005;
        public double CurbOpeningHeightFt { get; private set; } = 0.5;

        public InletOptionsDialog()
        {
            Title = "HydroComplete — HEC-22 Inlet";
            Width = 420;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(12) };
            for (int i = 0; i < 8; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int row = 0;
            AddRow(grid, row++, "Inlet type", _typeCombo = new ComboBox
            {
                Items = { "Grate on grade", "Sag grate", "Curb opening" },
                SelectedIndex = 0,
            });

            AddRow(grid, row++, "Length L (ft)", _lengthBox = MakeBox("5"));
            AddRow(grid, row++, "Gutter depth d (ft)", _depthBox = MakeBox("0.15"));
            AddRow(grid, row++, "Gutter slope S", _slopeBox = MakeBox("0.005"));
            AddRow(grid, row++, "Curb opening height a (ft)", _curbHeightBox = MakeBox("0.5"));

            _previewText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 8),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(_previewText, row++);
            grid.Children.Add(_previewText);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Run check", Width = 90, Margin = new Thickness(4), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(4), IsCancel = true };
            ok.Click += (_, _) => { if (TryRead()) { DialogResult = true; Close(); } };
            cancel.Click += (_, _) => { DialogResult = false; Close(); };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, row);
            grid.Children.Add(buttons);

            _typeCombo.SelectionChanged += (_, _) => UpdatePreview();
            foreach (TextBox box in new[] { _lengthBox, _depthBox, _slopeBox, _curbHeightBox })
                box.TextChanged += (_, _) => UpdatePreview();

            Content = grid;
            UpdatePreview();
        }

        private bool TryRead()
        {
            SelectedType = _typeCombo.SelectedIndex switch
            {
                1 => InletCapacity.InletType.Sag,
                2 => InletCapacity.InletType.CurbOpening,
                _ => InletCapacity.InletType.GrateOnGrade,
            };

            if (!TryParse(_lengthBox.Text, out double length) || length <= 0) return false;
            if (!TryParse(_depthBox.Text, out double depth) || depth <= 0) return false;
            if (!TryParse(_slopeBox.Text, out double slope) || slope <= 0) return false;

            GrateLengthFt = length;
            FlowDepthFt = depth;
            GutterSlope = slope;

            if (SelectedType == InletCapacity.InletType.CurbOpening)
            {
                if (!TryParse(_curbHeightBox.Text, out double curb) || curb <= 0) return false;
                CurbOpeningHeightFt = curb;
            }

            return true;
        }

        private void UpdatePreview()
        {
            if (!TryParse(_lengthBox.Text, out double l)) l = 5;
            if (!TryParse(_depthBox.Text, out double d)) d = 0.15;
            if (!TryParse(_slopeBox.Text, out double s)) s = 0.005;
            var type = _typeCombo.SelectedIndex switch
            {
                1 => InletCapacity.InletType.Sag,
                2 => InletCapacity.InletType.CurbOpening,
                _ => InletCapacity.InletType.GrateOnGrade,
            };
            double curb = TryParse(_curbHeightBox.Text, out double c) ? c : 0.5;
            double q = InletCapacity.CapacityCfs(type, l, d, s, curb);
            _previewText.Text = $"Preview capacity: {q.ToString("0.00", CultureInfo.InvariantCulture)} cfs";
            _curbHeightBox.IsEnabled = type == InletCapacity.InletType.CurbOpening;
        }

        private static TextBox MakeBox(string text) => new TextBox { Text = text, Margin = new Thickness(0, 2, 0, 6) };

        private static void AddRow(Grid grid, int row, string label, UIElement input)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(input);
            Grid.SetRow(panel, row);
            grid.Children.Add(panel);
        }

        private static bool TryParse(string text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}