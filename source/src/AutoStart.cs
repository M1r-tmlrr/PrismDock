using System;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace PrismDock
{
    /// Starts PrismDock right at sign-in through a per-user Task Scheduler logon task.
    /// The classic Run key is started by explorer after a startup delay and behind every other startup app
    /// (measured ~17 s here); the logon task starts immediately. No admin rights needed.
    static class AutoStart
    {
        // "PrismDock" was the first name; a security tool can delete a task's file but leave its registration,
        // after which that name can be neither replaced nor deleted without admin rights -> new name, old one cleaned up
        const string TaskName = "PrismDock Autostart";
        const string LegacyTask = "PrismDock";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool RunKeyExists()
        {
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("PrismDock") != null; }
            catch { return false; }
        }

        static void RemoveRunKey()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (k != null && k.GetValue("PrismDock") != null) k.DeleteValue("PrismDock");
            }
            catch { }
        }

        static string Schtasks(string args, out int code)
        {
            code = -1;
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    code = p.ExitCode;
                    return o;
                }
            }
            catch { return ""; }
        }

        /// path of the exe the logon task starts, or null when there is no task
        public static string TaskExe() { return TaskExe(TaskName); }
        static string TaskExe(string name)
        {
            int code;
            string xml = Schtasks("/query /tn \"" + name + "\" /xml", out code);
            if (code != 0) return null;
            int a = xml.IndexOf("<Command>", StringComparison.Ordinal), b = xml.IndexOf("</Command>", StringComparison.Ordinal);
            if (a < 0 || b < a) return "";
            return SecurityElement.FromString("<x>" + xml.Substring(a + 9, b - a - 9) + "</x>").Text.Trim('"');
        }

        public static bool CreateTask(string exe)
        {
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            string xml =
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo><Description>PrismDock</Description></RegistrationInfo>\r\n" +
"  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(user) + "</UserId></LogonTrigger></Triggers>\r\n" +
"  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\r\n" +
"  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
"<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority>" +
"<StartWhenAvailable>false</StartWhenAvailable><AllowHardTerminate>true</AllowHardTerminate></Settings>\r\n" +
"  <Actions Context=\"Author\"><Exec><Command>\"" + SecurityElement.Escape(exe) + "\"</Command><WorkingDirectory>" + SecurityElement.Escape(Path.GetDirectoryName(exe)) + "</WorkingDirectory></Exec></Actions>\r\n" +
"</Task>\r\n";
            string f = Path.Combine(Path.GetTempPath(), "prismdock-task.xml");
            try
            {
                File.WriteAllText(f, xml, System.Text.Encoding.Unicode);
                int code;
                Schtasks("/create /tn \"" + TaskName + "\" /xml \"" + f + "\" /f", out code);
                return code == 0;
            }
            catch { return false; }
            finally { try { File.Delete(f); } catch { } }
        }

        public static void DeleteTask()
        {
            int code;
            Schtasks("/delete /tn \"" + TaskName + "\" /f", out code);
            Schtasks("/delete /tn \"" + LegacyTask + "\" /f", out code);
        }

        /// turn on / off (also cleans up the old Run key so it can't start twice)
        public static bool Set(bool on, string exe)
        {
            if (on)
            {
                bool ok = CreateTask(exe);
                if (ok) RemoveRunKey();
                return ok;
            }
            DeleteTask();
            RemoveRunKey();
            return true;
        }

        /// on startup: move an old Run-key autostart over to the logon task, fix the path if the exe moved.
        /// returns whether autostart is on.
        public static bool Migrate(string exe) { return Migrate(exe, false); }

        /// wanted: the user's setting says "start with Windows" (re-creates the task if e.g. a security tool deleted it)
        public static bool Migrate(string exe, bool wanted)
        {
            bool run = RunKeyExists();
            string task = TaskExe();
            if (TaskExe(LegacyTask) != null)
            {
                // move an install of 2.1.1/2.1.2 over to the new task name
                int code;
                if (CreateTask(exe)) { Schtasks("/delete /tn \"" + LegacyTask + "\" /f", out code); EventLog.Write("autostart: task renamed"); }
                return true;
            }
            if (!run && task == null && wanted)
            {
                bool ok = CreateTask(exe);
                EventLog.Write("autostart: logon task was missing -> " + (ok ? "re-created" : "could not re-create"));
                return ok;
            }
            if (run)
            {
                if (CreateTask(exe)) { RemoveRunKey(); EventLog.Write("autostart: Run key -> logon task"); }
                return true;
            }
            if (task == null) return false;
            if (!string.Equals(task, exe, StringComparison.OrdinalIgnoreCase)) { CreateTask(exe); EventLog.Write("autostart: task path updated"); }
            return true;
        }
    }
}
