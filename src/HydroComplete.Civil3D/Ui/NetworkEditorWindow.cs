using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using HydroComplete.Civil3D.Reading;

namespace HydroComplete.Civil3D.Ui
{
    internal sealed class NetworkEditorWindow : Window
    {
        private readonly ObservableCollection<PipeEditRow> _rows;

        public IReadOnlyList<PipeEditRow> Rows => _rows;

        public NetworkEditorWindow(IEnumerable<ReadPipe> pipes)
        {
            Title = "HydroComplete — Network Editor";
            Width = 820;
            Height = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _rows = new ObservableCollection<PipeEditRow>();
            foreach (ReadPipe pipe in pipes.OrderBy(p => p.NetworkName).ThenBy(p => p.PipeName))
            {
                _rows.Add(new PipeEditRow
                {
                    PipeKey = PipeKey(pipe),
                    NetworkName = pipe.NetworkName,
                    PipeName = pipe.PipeName,
                    LengthFt = pipe.LengthFt,
                    DiameterFt = pipe.Segment.DiameterFt,
                    DesignFlowCfs = pipe.Segment.DesignFlowCfs > 0 ? pipe.Segment.DesignFlowCfs : (double?)null,
                    ManningN = pipe.Segment.ManningN,
                });
            }

            var grid = new Grid { Margin = new Thickness(8) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var table = new DataGrid
            {
                ItemsSource = _rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                Margin = new Thickness(0, 0, 0, 8),
            };
            table.Columns.Add(new DataGridTextColumn { Header = "Network", Binding = new System.Windows.Data.Binding(nameof(PipeEditRow.NetworkName)), IsReadOnly = true, Width = 110 });
            table.Columns.Add(new DataGridTextColumn { Header = "Pipe", Binding = new System.Windows.Data.Binding(nameof(PipeEditRow.PipeName)), IsReadOnly = true, Width = 120 });
            table.Columns.Add(new DataGridTextColumn { Header = "Length (ft)", Binding = new System.Windows.Data.Binding(nameof(PipeEditRow.LengthFt)) { StringFormat = "0.0" }, IsReadOnly = true, Width = 80 });
            table.Columns.Add(new DataGridTextColumn { Header = "Dia (ft)", Binding = new System.Windows.Data.Binding(nameof(PipeEditRow.DiameterFt)) { StringFormat = "0.00" }, IsReadOnly = true, Width = 70 });
            table.Columns.Add(new DataGridTextColumn { Header = "Design Q (cfs)", Binding = new System.Windows.Data.Binding(nameof(PipeEditRow.DesignFlowCfs)), Width = 100 });
            table.Columns.Add(new DataGridTextColumn { Header = "Manning n", Binding = new System.Windows.Data.Binding(nameof(PipeEditRow.ManningN)), Width = 80 });
            table.Columns.Add(new DataGridTextColumn { Header = "Notes", Binding = new System.Windows.Data.Binding(nameof(PipeEditRow.Notes)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            Grid.SetRow(table, 0);
            grid.Children.Add(table);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "Save overrides", Width = 110, Margin = new Thickness(4), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(4), IsCancel = true };
            save.Click += (_, _) => { DialogResult = true; Close(); };
            cancel.Click += (_, _) => { DialogResult = false; Close(); };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 1);
            grid.Children.Add(buttons);

            Content = grid;
        }

        private static string PipeKey(ReadPipe pipe)
            => string.IsNullOrEmpty(pipe.PipeName) ? pipe.PipeId.Handle.ToString() : pipe.PipeName;
    }

    internal sealed class PipeEditRow : INotifyPropertyChanged
    {
        private double? _designFlowCfs;
        private double _manningN = 0.013;
        private string _notes = "";

        public string PipeKey { get; set; } = "";
        public string NetworkName { get; set; } = "";
        public string PipeName { get; set; } = "";
        public double LengthFt { get; set; }
        public double DiameterFt { get; set; }

        public double? DesignFlowCfs
        {
            get => _designFlowCfs;
            set { _designFlowCfs = value; OnPropertyChanged(); }
        }

        public double ManningN
        {
            get => _manningN;
            set { _manningN = value; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value ?? ""; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}