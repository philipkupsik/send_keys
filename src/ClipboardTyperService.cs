using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;

namespace ClipboardTyper
{
    /// <summary>
    /// Windows-Dienst. Dienste laufen in Sitzung 0 und haben weder Zugriff auf die
    /// Zwischenablage noch auf die Tastatur der angemeldeten Benutzer. Der Dienst startet
    /// deshalb in jeder Benutzersitzung einen unsichtbaren Agenten (dieselbe EXE mit
    /// "--agent"), der den Hotkey registriert und die Eingaben sendet. Beendet sich ein
    /// Agent, wird er automatisch neu gestartet.
    /// </summary>
    public class ClipboardTyperService : ServiceBase
    {
        public const string Name = "ClipboardTyper";
        private const int CheckIntervalMs = 5000;

        private readonly Dictionary<int, IntPtr> agents = new Dictionary<int, IntPtr>();
        private readonly object sync = new object();
        private Timer timer;
        private bool stopping;

        public ClipboardTyperService()
        {
            ServiceName = Name;
            CanHandleSessionChangeEvent = true;
            CanStop = true;
            CanShutdown = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            stopping = false;
            timer = new Timer(delegate { EnsureAgents(); }, null, 0, CheckIntervalMs);
        }

        protected override void OnStop()
        {
            StopAll();
        }

        protected override void OnShutdown()
        {
            StopAll();
        }

        protected override void OnSessionChange(SessionChangeDescription change)
        {
            switch (change.Reason)
            {
                case SessionChangeReason.SessionLogoff:
                    lock (sync) { RemoveAgent(change.SessionId, true); }
                    break;
                case SessionChangeReason.SessionLogon:
                case SessionChangeReason.ConsoleConnect:
                case SessionChangeReason.RemoteConnect:
                case SessionChangeReason.SessionUnlock:
                    ThreadPool.QueueUserWorkItem(delegate { EnsureAgents(); });
                    break;
            }
        }

        private void StopAll()
        {
            lock (sync)
            {
                stopping = true;
                if (timer != null)
                {
                    timer.Dispose();
                    timer = null;
                }
                foreach (int sessionId in new List<int>(agents.Keys))
                    RemoveAgent(sessionId, true);
            }
        }

        /// <summary>Startet in jeder Benutzersitzung ohne laufenden Agenten einen neuen.</summary>
        private void EnsureAgents()
        {
            lock (sync)
            {
                if (stopping)
                    return;

                // Beendete Agenten aufräumen
                foreach (KeyValuePair<int, IntPtr> entry in new List<KeyValuePair<int, IntPtr>>(agents))
                {
                    if (NativeMethods.WaitForSingleObject(entry.Value, 0) != NativeMethods.WAIT_TIMEOUT)
                        RemoveAgent(entry.Key, false);
                }

                foreach (int sessionId in GetUserSessions())
                {
                    if (agents.ContainsKey(sessionId))
                        continue;
                    try
                    {
                        agents[sessionId] = StartAgentInSession(sessionId);
                    }
                    catch (Win32Exception ex)
                    {
                        // 1008 = ERROR_NO_TOKEN: Sitzung ohne angemeldeten Benutzer (Anmeldebildschirm)
                        if (ex.NativeErrorCode != 1008)
                            Log("Agent konnte in Sitzung " + sessionId + " nicht gestartet werden: " + ex.Message,
                                EventLogEntryType.Warning);
                    }
                    catch (Exception ex)
                    {
                        Log("Agent konnte in Sitzung " + sessionId + " nicht gestartet werden: " + ex.Message,
                            EventLogEntryType.Warning);
                    }
                }
            }
        }

        private void RemoveAgent(int sessionId, bool terminate)
        {
            IntPtr hProcess;
            if (!agents.TryGetValue(sessionId, out hProcess))
                return;
            if (terminate)
                NativeMethods.TerminateProcess(hProcess, 0);
            NativeMethods.CloseHandle(hProcess);
            agents.Remove(sessionId);
        }

        private static List<int> GetUserSessions()
        {
            List<int> result = new List<int>();
            IntPtr buffer;
            int count;
            if (!NativeMethods.WTSEnumerateSessions(NativeMethods.WTS_CURRENT_SERVER_HANDLE, 0, 1, out buffer, out count))
                return result;
            try
            {
                int size = Marshal.SizeOf(typeof(NativeMethods.WTS_SESSION_INFO));
                for (int i = 0; i < count; i++)
                {
                    NativeMethods.WTS_SESSION_INFO info = (NativeMethods.WTS_SESSION_INFO)Marshal.PtrToStructure(
                        new IntPtr(buffer.ToInt64() + i * size), typeof(NativeMethods.WTS_SESSION_INFO));
                    // Sitzung 0 ist die Dienst-Sitzung; nur aktive/getrennte Benutzersitzungen
                    if (info.SessionId != 0 &&
                        (info.State == NativeMethods.WTS_CONNECTSTATE_CLASS.WTSActive ||
                         info.State == NativeMethods.WTS_CONNECTSTATE_CLASS.WTSDisconnected))
                        result.Add(info.SessionId);
                }
            }
            finally
            {
                NativeMethods.WTSFreeMemory(buffer);
            }
            return result;
        }

        /// <summary>
        /// Startet den Agenten mit dem Token des angemeldeten Benutzers auf dessen Desktop.
        /// Ist der Benutzer Administrator (UAC), wird das erhöhte Token verwendet, damit
        /// auch in Fenster mit Administratorrechten getippt werden kann.
        /// </summary>
        private static IntPtr StartAgentInSession(int sessionId)
        {
            IntPtr userToken = IntPtr.Zero, primaryToken = IntPtr.Zero, environment = IntPtr.Zero;
            try
            {
                if (!NativeMethods.WTSQueryUserToken(sessionId, out userToken))
                    throw new Win32Exception(); // z. B. kein Benutzer angemeldet

                IntPtr token = userToken;
                IntPtr linkedToken = GetElevatedLinkedToken(userToken);
                if (linkedToken != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(userToken);
                    userToken = linkedToken;
                    token = linkedToken;
                }

                if (!NativeMethods.DuplicateTokenEx(token, NativeMethods.MAXIMUM_ALLOWED, IntPtr.Zero,
                        NativeMethods.SecurityImpersonation, NativeMethods.TokenPrimary, out primaryToken))
                    throw new Win32Exception();

                if (!NativeMethods.CreateEnvironmentBlock(out environment, primaryToken, false))
                    environment = IntPtr.Zero;

                string exe = Process.GetCurrentProcess().MainModule.FileName;
                NativeMethods.STARTUPINFO si = new NativeMethods.STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO));
                si.lpDesktop = @"winsta0\default";

                NativeMethods.PROCESS_INFORMATION pi;
                uint flags = NativeMethods.CREATE_NO_WINDOW |
                             (environment != IntPtr.Zero ? NativeMethods.CREATE_UNICODE_ENVIRONMENT : 0);
                if (!NativeMethods.CreateProcessAsUser(primaryToken, exe, "\"" + exe + "\" --agent",
                        IntPtr.Zero, IntPtr.Zero, false, flags, environment,
                        System.IO.Path.GetDirectoryName(exe), ref si, out pi))
                    throw new Win32Exception();

                NativeMethods.CloseHandle(pi.hThread);
                return pi.hProcess;
            }
            finally
            {
                if (environment != IntPtr.Zero) NativeMethods.DestroyEnvironmentBlock(environment);
                if (primaryToken != IntPtr.Zero) NativeMethods.CloseHandle(primaryToken);
                if (userToken != IntPtr.Zero) NativeMethods.CloseHandle(userToken);
            }
        }

        private static IntPtr GetElevatedLinkedToken(IntPtr token)
        {
            int elevationType, len;
            if (!NativeMethods.GetTokenInformation(token, NativeMethods.TOKEN_INFORMATION_CLASS.TokenElevationType,
                    out elevationType, sizeof(int), out len) ||
                elevationType != (int)NativeMethods.TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited)
                return IntPtr.Zero;

            IntPtr linked;
            if (!NativeMethods.GetTokenInformation(token, NativeMethods.TOKEN_INFORMATION_CLASS.TokenLinkedToken,
                    out linked, IntPtr.Size, out len))
                return IntPtr.Zero;
            return linked;
        }

        private void Log(string message, EventLogEntryType type)
        {
            try { EventLog.WriteEntry(message, type); }
            catch { }
        }
    }
}
