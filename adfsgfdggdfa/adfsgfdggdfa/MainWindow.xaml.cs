using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RumogloBot;

public partial class MainWindow : Window
{
    // A robot jelei az irány sorrendjében: kelet, észak, nyugat, dél (ugyanaz a sorrend, mint az Interpreter.Direction).
    const string RobotSymbols = "►▲◄▼";

    static readonly Brush YellowBrush = new SolidColorBrush(Color.FromRgb(255, 204, 0));
    static readonly Brush ExploredBrush = new SolidColorBrush(Color.FromRgb(51, 51, 51));

    DispatcherTimer timer = new DispatcherTimer();   // a lassított futás (run) órajele
    Border[,] cellBorders;                           // a térkép cellái (háttér)
    TextBlock[,] cellTexts;                          // a térkép cellái (jel)

    public MainWindow()
    {
        InitializeComponent();
        BuildMap();

        timer.Interval = TimeSpan.FromMilliseconds(Program.stepDelayMilliseconds);
        timer.Tick += Timer_Tick;
        timer.Start();

        OutputBox.Text = "MineScript IDE – írj be egy parancsot, vagy használd a gombokat. A 'help' mindent kilistáz.";
        RefreshAll();
        InputBox.Focus();
    }

    // ---------- Térkép ----------

    // A térkép celláinak létrehozása (egyszer, indításkor): sor × oszlop mérete az input fájlból jön.
    void BuildMap()
    {
        int rows = Program.interpreter.Rows;
        int columns = Program.interpreter.Columns;

        MapGrid.Rows = rows;
        MapGrid.Columns = columns;
        cellBorders = new Border[rows, columns];
        cellTexts = new TextBlock[rows, columns];

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                TextBlock text = new TextBlock();
                text.FontSize = 22;
                text.FontWeight = FontWeights.Bold;
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.VerticalAlignment = VerticalAlignment.Center;

                Border border = new Border();
                border.Width = 40;
                border.Height = 40;
                border.BorderBrush = YellowBrush;
                border.BorderThickness = new Thickness(0.5);
                border.Child = text;

                MapGrid.Children.Add(border);
                cellBorders[row, column] = border;
                cellTexts[row, column] = text;
            }
        }
    }

    // A cellák jele: '?' = még ismeretlen, egyébként a cella tartalma (. # @); a robot nyíl sárga háttéren.
    void RefreshMap()
    {
        Interpreter robot = Program.interpreter;

        for (int row = 0; row < robot.Rows; row++)
        {
            for (int column = 0; column < robot.Columns; column++)
            {
                Border border = cellBorders[row, column];
                TextBlock text = cellTexts[row, column];

                if (row == robot.RobotRow && column == robot.RobotColumn)
                {
                    text.Text = RobotSymbols[robot.Direction].ToString();
                    text.Foreground = Brushes.Black;
                    border.Background = YellowBrush;
                    continue;
                }

                char symbol = Program.GetMapSymbol(row, column);
                text.Text = symbol.ToString();

                if (robot.Explored[row, column])
                {
                    border.Background = ExploredBrush;
                }
                else
                {
                    border.Background = Brushes.Black;
                }

                if (symbol == '@')
                {
                    text.Foreground = YellowBrush;
                }
                else
                {
                    text.Foreground = Brushes.White;
                }
            }
        }
    }

    // A térkép legfeljebb az ablak magasságának 40%-át foglalja el; ha nagyobb, kicsinyítve fér el.
    void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        MapBox.MaxHeight = e.NewSize.Height * 0.4;
    }

    // ---------- Frissítés ----------

    // Minden megjelenített adat frissítése a program és az interpreter jelenlegi állapotából.


    void RefreshAll()
    {
        string frontDescription = Program.DescribeFrontCell();
        SensorRun.Text = frontDescription;
        Interpreter robot = Program.interpreter;

        if (frontDescription.ToLower().Contains("aranyrög"))
        {
            SensorRun.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#FFCC00");
        }
        else
        {
            SensorRun.Foreground = System.Windows.Media.Brushes.Black;
        }

            GoldRun.Text = robot.CollectedGold.ToString();
        VariablesLabel.Text = "Változók: " + Program.DescribeVariables();
        ConsoleLabel.Text = "Konzol: " + Program.DescribeConsoleOutput();

        string status = Program.StatusText;
        if (robot.ErrorCode != null)
        {
            status = status + " HIBA: " + robot.ErrorCode;
        }
        StatusLabel.Text = "Állapot: " + status;

        RefreshMap();
        TextViewBox.Text = string.Join("\n", Program.GetTextViewLines());
        BlockViewBox.Text = string.Join("\n", Program.GetBlockViewLines());
        ShowMessages();
    }

    // A Program üzeneteinek (help, hibák, napló stb.) kiírása a Command bar kimenetére.
    void ShowMessages()
    {
        foreach (string message in Program.TakeMessages())
        {
            AddOutput(message);
        }
    }

    void AddOutput(string text)
    {
        OutputBox.AppendText("\n" + text);
        OutputBox.ScrollToEnd();
    }

    // ---------- Lassított futás (run) ----------

    void Timer_Tick(object sender, EventArgs e)
    {
        // A "speed" parancs átállíthatja a késleltetést.
        TimeSpan newInterval = TimeSpan.FromMilliseconds(Program.stepDelayMilliseconds);
        if (timer.Interval != newInterval)
        {
            timer.Interval = newInterval;
        }

        if (Program.slowRunning)
        {
            Program.SlowRunTick();
            RefreshAll();
        }
    }

    // ---------- Parancsok ----------

    // Egy parancs végrehajtása: kiírja a parancsot, lefuttatja, majd frissít mindent.
    void RunCommand(string text)
    {
        AddOutput("> " + text);
        bool shouldExit = Program.ExecuteInput(text);
        RefreshAll();
        InputBox.Focus();

        if (shouldExit)
        {
            Close();
        }
    }

    // Enter: a beírt parancs végrehajtása.
    void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        string text = InputBox.Text.Trim();
        InputBox.Clear();
        if (text.Length > 0)
        {
            RunCommand(text);
        }
    }

    void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        RunCommand("help");
    }

    void RunButton_Click(object sender, RoutedEventArgs e)
    {
        RunCommand("run");
    }

    void DebugButton_Click(object sender, RoutedEventArgs e)
    {
        RunCommand("debug");
    }

    void NextButton_Click(object sender, RoutedEventArgs e)
    {
        RunCommand("n");
    }

    void StopButton_Click(object sender, RoutedEventArgs e)
    {
        RunCommand("stop");
    }
}
