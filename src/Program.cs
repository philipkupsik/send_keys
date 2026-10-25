using System;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace ClipboardTyper
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("--agent", StringComparison.OrdinalIgnoreCase))
                return RunAgent();

            if (!Environment.UserInteractive)
            {
                ServiceBase.Run(new ClipboardTyperService());
                return 0;
            }

            MessageBox.Show(
                "Sendet den Text der Zwischenablage als Tastatureingabe.\n" +
                "Tastenkombination: Strg+Alt+Umschalt+Einfg (Esc bricht das Tippen ab)\n\n" +
                "install.cmd\t\tals Windows-Dienst installieren und starten\n" +
                "uninstall.cmd\t\tDienst entfernen\n" +
                "ClipboardTyper.exe --agent\tohne Dienst in dieser Sitzung testen",
                "ClipboardTyper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        private static int RunAgent()
        {
            bool createdNew;
            // Pro Sitzung nur ein Agent ("Local\" = sitzungsweit)
            using (Mutex mutex = new Mutex(true, @"Local\ClipboardTyperAgent", out createdNew))
            {
                if (!createdNew)
                    return 0;
                Application.EnableVisualStyles();
                using (Agent agent = new Agent())
                {
                    Application.Run();
                }
            }
            return 0;
        }
    }
}
