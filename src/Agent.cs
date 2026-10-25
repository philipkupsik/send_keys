using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ClipboardTyper
{
    /// <summary>
    /// Läuft unsichtbar in der Benutzersitzung, registriert Strg+Alt+Umschalt+Einfg und
    /// sendet bei Betätigung den Text der Zwischenablage als Tastatureingabe (siehe KeyboardSender).
    /// </summary>
    internal sealed class Agent : NativeWindow, IDisposable
    {
        private const int HotkeyId = 1;
        private const int DelayBetweenKeysMs = 10;   // Pause zwischen den Zeichen
        private const int MaxChars = 20000;          // Schutz vor versehentlich riesigen Inhalten

        private readonly System.Windows.Forms.Timer retryTimer = new System.Windows.Forms.Timer();
        private bool registered;
        private bool busy;

        public Agent()
        {
            CreateHandle(new CreateParams()); // unsichtbares Nachrichtenfenster
            retryTimer.Interval = 5000;
            retryTimer.Tick += delegate { TryRegister(); };
            TryRegister();
        }

        private void TryRegister()
        {
            registered = NativeMethods.RegisterHotKey(Handle, HotkeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
                NativeMethods.VK_INSERT);
            if (registered)
            {
                retryTimer.Stop();
                Log("Hotkey Strg+Alt+Umschalt+Einfg registriert.");
            }
            else if (!retryTimer.Enabled)
            {
                // z. B. weil ein anderes Programm die Kombination bereits belegt
                Log("Hotkey konnte nicht registriert werden (Fehler " +
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error() + "), neuer Versuch alle 5 s.");
                retryTimer.Start();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                if (!busy)
                {
                    busy = true;
                    try { TypeClipboard(); }
                    catch (Exception ex) { Log("Fehler beim Senden: " + ex); }
                    finally { busy = false; }
                }
                return;
            }
            base.WndProc(ref m);
        }

        private void TypeClipboard()
        {
            if (!Clipboard.ContainsText())
                return;
            string text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (string.IsNullOrEmpty(text))
                return;
            if (text.Length > MaxChars)
            {
                Log("Zwischenablage zu groß (" + text.Length + " Zeichen), abgebrochen.");
                return;
            }

            // Erst senden, wenn Strg/Alt/Umschalt/Einfg losgelassen wurden – sonst würden
            // die getippten Zeichen als Tastenkombinationen interpretiert.
            WaitForKeysReleased(5000);

            // Zeilenumbrüche vereinheitlichen: \r\n und \r -> \n
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            IntPtr layout = KeyboardSender.GetTargetLayout();

            for (int i = 0; i < text.Length; i++)
            {
                if (IsDown(NativeMethods.VK_ESCAPE))
                {
                    Log("Senden mit Esc abgebrochen.");
                    break;
                }
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    KeyboardSender.SendSurrogatePair(c, text[++i]);
                else if (c == '\n' || c == '\t' || !char.IsControl(c))
                    KeyboardSender.SendChar(c, layout);
                Thread.Sleep(DelayBetweenKeysMs);
            }
        }

        private static void WaitForKeysReleased(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs &&
                   (IsDown(NativeMethods.VK_CONTROL) || IsDown(NativeMethods.VK_MENU) ||
                    IsDown(NativeMethods.VK_SHIFT) || IsDown(NativeMethods.VK_INSERT)))
            {
                Thread.Sleep(20);
                waited += 20;
            }
            Thread.Sleep(50);
        }

        private static bool IsDown(int vk)
        {
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        public void Dispose()
        {
            retryTimer.Dispose();
            if (registered)
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }

        internal static void Log(string message)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ClipboardTyper");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "agent.log");
                if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024)
                    File.Delete(file);
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message +
                    Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
