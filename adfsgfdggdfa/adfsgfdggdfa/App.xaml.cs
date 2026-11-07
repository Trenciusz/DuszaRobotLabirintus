using System;
using System.Windows;

namespace RumogloBot;

public partial class App : Application
{
    // Indítás: robot.exe input.txt output.txt   vagy   robot.exe -q input.txt output.txt
    // Csendes módban (-q) nem nyílik ablak: a program lefut, a napló kiíródik, és a program kilép.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            bool parametersAreOk = Program.Initialize(e.Args);
            if (!parametersAreOk)
            {
                MessageBox.Show("Használat: robot [-q] input.txt output.txt", "RUMOGLÓBOT");
                Shutdown(1);
                return;
            }

            if (Program.QuietMode)
            {
                Program.RunQuiet();
                Shutdown(0);
                return;
            }
        }
        catch (Exception exception)
        {
            if (!Program.QuietMode)
            {
                MessageBox.Show("Hiba az indításkor: " + exception.Message, "RUMOGLÓBOT");
            }
            Shutdown(1);
            return;
        }

        MainWindow window = new MainWindow();
        window.Show();
    }
}
