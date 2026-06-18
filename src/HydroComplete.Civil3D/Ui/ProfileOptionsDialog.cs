using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HydroComplete.Civil3D.Reading;

namespace HydroComplete.Civil3D.Ui
{
    internal sealed class ProfileOptionsDialog : Window
    {
        private readonly ComboBox _networkCombo;
        private readonly TextBox _horizBox;
        private readonly TextBox _vertBox;
        private readonly TextBox _datumBox;
        private readonly TextBox _tailwaterBox;
        private readonly CheckBox _hglCheck;
        private readonly CheckBox _drawCheck;
        private readonly CheckBox _exportDxfCheck;

        public NetworkTopology.OrderedNetwork? SelectedNetwork { get; private set; }
        public double HorizontalScale { get; private set; } = 20.0;
        public double VerticalScale { get; private set; } = 20.0;
        public double DatumFt { get; private set; }
        public double TailwaterFt { get; private set; }
        public bool IncludeHgl { get; private set; }
        public bool DrawToDrawing { get; private set; } = true;
        public bool ExportDxf { get; private set; }

        public ProfileOptionsDialog(
            IReadOnlyList<NetworkTopology.OrderedNetwork> networks,
            double defaultDatum,
            double defaultTailwater)
        {
            Title = "HydroComplete — Chainage Profile";
            Width = 440;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(12) };
            for (int i = 0; i < 10; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int row = 0;
            _networkCombo = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            foreach (NetworkTopology.OrderedNetwork net in networks)
                _networkCombo.Items.Add(net.NetworkName);
            if (_networkCombo.Items.Count > 0) _networkCombo.SelectedIndex = 0;
            AddRow(grid, row++, "Network", _networkCombo);

            AddRow(grid, row++, "Horizontal scale (ft/dwg-ft)", _horizBox = MakeBox("20"));
            AddRow(grid, row++, "Vertical scale (ft/dwg-ft)", _vertBox = MakeBox("20"));
            AddRow(grid, row++, "Datum elevation (ft)", _datumBox = MakeBox(defaultDatum.ToString("0.00", CultureInfo.InvariantCulture)));
            _hglCheck = new CheckBox { Content = "Include HGL (steady backwater)", Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(_hglCheck, row++);
            grid.Children.Add(_hglCheck);
            AddRow(grid, row++, "Outfall tailwater HGL (ft)", _tailwaterBox = MakeBox(defaultTailwater.ToString("0.00", CultureInfo.InvariantCulture)));
            _drawCheck = new CheckBox { Content = "Draw profile in drawing (HC-PROFILE-* layers)", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(_drawCheck, row++);
            grid.Children.Add(_drawCheck);
            _exportDxfCheck = new CheckBox { Content = "Also export DXF to Documents/HydroComplete", Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(_exportDxfCheck, row++);
            grid.Children.Add(_exportDxfCheck);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Run", Width = 80, Margin = new Thickness(4), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(4), IsCancel = true };
            ok.Click += (_, _) => { if (TryRead(networks)) { DialogResult = true; Close(); } };
            cancel.Click += (_, _) => { DialogResult = false; Close(); };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, row);
            grid.Children.Add(buttons);

            Content = grid;
        }

        private bool TryRead(IReadOnlyList<NetworkTopology.OrderedNetwork> networks)
        {
            if (_networkCombo.SelectedItem is not string name) return false;
            SelectedNetwork = networks.FirstOrDefault(n =>
                string.Equals(n.NetworkName, name, StringComparison.OrdinalIgnoreCase));
            if (SelectedNetwork == null) return false;

            if (!TryParse(_horizBox.Text, out double h) || h <= 0) return false;
            if (!TryParse(_vertBox.Text, out double v) || v <= 0) return false;
            if (!TryParse(_datumBox.Text, out double datum)) return false;
            if (!TryParse(_tailwaterBox.Text, out double tw)) return false;

            HorizontalScale = h;
            VerticalScale = v;
            DatumFt = datum;
            TailwaterFt = tw;
            IncludeHgl = _hglCheck.IsChecked == true;
            DrawToDrawing = _drawCheck.IsChecked == true;
            ExportDxf = _exportDxfCheck.IsChecked == true;
            return DrawToDrawing || ExportDxf;
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