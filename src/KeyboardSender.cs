using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipboardTyper
{
    /// <summary>
    /// Tippt Text wie eine echte Tastatur: Für jedes Zeichen wird im Tastaturlayout des
    /// Zielfensters die passende Taste samt Umschalt/AltGr ermittelt und mit virtuellem
    /// Tastencode UND Scancode gesendet (wichtig für VM-Konsolen und Remote-Sitzungen).
    /// Tottasten (z. B. ^ ` ´ im deutschen Layout) werden mit einem Leerzeichen
    /// abgeschlossen, damit sie nicht mit dem nächsten Zeichen kombiniert werden.
    /// Zeichen, die im Layout nicht vorkommen, werden als Unicode-Eingabe gesendet.
    /// </summary>
    internal static class KeyboardSender
    {
        public static IntPtr GetTargetLayout()
        {
            uint thread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), IntPtr.Zero);
            return NativeMethods.GetKeyboardLayout(thread);
        }

        public static void SendChar(char c, IntPtr layout)
        {
            List<NativeMethods.INPUT> inputs = new List<NativeMethods.INPUT>();
            switch (c)
            {
                case '\n':
                    AddKey(inputs, NativeMethods.VK_RETURN, layout);
                    break;
                case '\t':
                    AddKey(inputs, NativeMethods.VK_TAB, layout);
                    break;
                default:
                    if (!AddCharKeys(inputs, c, layout))
                    {
                        inputs.Add(Unicode(c, false));
                        inputs.Add(Unicode(c, true));
                    }
                    break;
            }
            Send(inputs);
        }

        /// <summary>Für Zeichen außerhalb der Basisebene (z. B. Emoji): Surrogatpaar als Unicode.</summary>
        public static void SendSurrogatePair(char high, char low)
        {
            List<NativeMethods.INPUT> inputs = new List<NativeMethods.INPUT>();
            inputs.Add(Unicode(high, false));
            inputs.Add(Unicode(low, false));
            inputs.Add(Unicode(low, true));
            inputs.Add(Unicode(high, true));
            Send(inputs);
        }

        private static bool AddCharKeys(List<NativeMethods.INPUT> inputs, char c, IntPtr layout)
        {
            short scan = NativeMethods.VkKeyScanEx(c, layout);
            if (scan == -1)
                return false;
            int vk = scan & 0xFF;
            int shiftState = (scan >> 8) & 0xFF;
            bool shift = (shiftState & 1) != 0;
            bool ctrl = (shiftState & 2) != 0;
            bool alt = (shiftState & 4) != 0;
            if ((shiftState & ~7) != 0 || (ctrl != alt))
                return false; // Sonderzustände (Kana o. ä.) oder reines Strg/Alt -> Unicode

            bool altGr = ctrl && alt;
            if (altGr)
            {
                inputs.Add(Key(NativeMethods.VK_LCONTROL, layout, false));
                inputs.Add(Key(NativeMethods.VK_RMENU, layout, false));
            }
            if (shift)
                inputs.Add(Key(NativeMethods.VK_LSHIFT, layout, false));

            inputs.Add(Key(vk, layout, false));
            inputs.Add(Key(vk, layout, true));

            if (shift)
                inputs.Add(Key(NativeMethods.VK_LSHIFT, layout, true));
            if (altGr)
            {
                inputs.Add(Key(NativeMethods.VK_RMENU, layout, true));
                inputs.Add(Key(NativeMethods.VK_LCONTROL, layout, true));
            }

            if (IsDeadKey(vk, shift, altGr, layout))
                AddKey(inputs, NativeMethods.VK_SPACE, layout); // Tottaste + Leertaste = Zeichen selbst
            return true;
        }

        private static bool IsDeadKey(int vk, bool shift, bool altGr, IntPtr layout)
        {
            byte[] state = new byte[256];
            if (shift)
                state[NativeMethods.VK_SHIFT] = 0x80;
            if (altGr)
            {
                state[NativeMethods.VK_CONTROL] = 0x80;
                state[NativeMethods.VK_MENU] = 0x80;
            }
            StringBuilder buffer = new StringBuilder(8);
            uint scanCode = NativeMethods.MapVirtualKeyEx((uint)vk, NativeMethods.MAPVK_VK_TO_VSC, layout);
            // wFlags 0x4: Tastaturzustand des Threads nicht verändern (Windows 10 1607+)
            int result = NativeMethods.ToUnicodeEx((uint)vk, scanCode, state, buffer, buffer.Capacity, 0x4, layout);
            return result < 0;
        }

        private static void AddKey(List<NativeMethods.INPUT> inputs, int vk, IntPtr layout)
        {
            inputs.Add(Key(vk, layout, false));
            inputs.Add(Key(vk, layout, true));
        }

        private static NativeMethods.INPUT Key(int vk, IntPtr layout, bool up)
        {
            NativeMethods.INPUT input = new NativeMethods.INPUT();
            input.type = NativeMethods.INPUT_KEYBOARD;
            input.u.ki.wVk = (ushort)vk;
            input.u.ki.wScan = (ushort)NativeMethods.MapVirtualKeyEx((uint)vk, NativeMethods.MAPVK_VK_TO_VSC, layout);
            input.u.ki.dwFlags = (up ? NativeMethods.KEYEVENTF_KEYUP : 0) |
                                 (vk == NativeMethods.VK_RMENU ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0);
            return input;
        }

        private static NativeMethods.INPUT Unicode(char c, bool up)
        {
            NativeMethods.INPUT input = new NativeMethods.INPUT();
            input.type = NativeMethods.INPUT_KEYBOARD;
            input.u.ki.wScan = c;
            input.u.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0);
            return input;
        }

        private static void Send(List<NativeMethods.INPUT> inputs)
        {
            if (inputs.Count == 0)
                return;
            NativeMethods.INPUT[] array = inputs.ToArray();
            NativeMethods.SendInput((uint)array.Length, array, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }
    }
}
