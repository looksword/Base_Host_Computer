using System;
using System.IO;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lib;

namespace Base
{
    class Program
    {
        private static bool standalone = false;
        private static void Log(string str)
        {
            try
            {
                FileStream fs = new FileStream(Path.Combine(Environment.CurrentDirectory, "log.txt"), FileMode.Append);
                str = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + str + "\r\n";
                byte[] bs = Encoding.Default.GetBytes(str);
                fs.Write(bs, 0, bs.Length);
                fs.Flush();
                fs.Close();
                using (fs) { }
            }
            catch { }
        }
        private static void ShowMsg(string msg)
        {
            if (standalone)
            {
                Console.WriteLine(msg);
            }
            else
            {
                Log(msg);
            }
        }
        private static void dcOnMsg(StringMsgType msgType, string msg)
        {
            ShowMsg(msgType.ToString() + ": " + msg);
        }
        private static void OnStart(ref object usesrState)
        {
            if (usesrState != null)
            {
                Base dc = (Base)usesrState;
                dc.Start();
                ShowMsg("开始工作");
            }
        }

        private static void OnStop(ref object usesrState)
        {
            if (usesrState != null)
            {
                Base dc = (Base)usesrState;
                dc.Stop();
                dc.OnMsg -= dcOnMsg;
                using (dc) { }
                ShowMsg("停止工作...");
            }
        }
        static void Main(string[] args)
        {
            /** 
            * 当前用户是管理员的时候，直接启动应用程序 
            * 如果不是管理员，则使用启动对象启动程序，以确保使用管理员身份运行 
            */
            //获得当前登录的Windows用户标示  
            System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            System.Security.Principal.WindowsPrincipal principal = new System.Security.Principal.WindowsPrincipal(identity);
            //判断当前登录用户是否为管理员  
            if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                //如果是管理员，则直接运行  
            }
            else//否则以管理员身份重新运行
            {
                //创建启动对象  
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                //设置运行文件  
                startInfo.FileName = Assembly.GetExecutingAssembly().Location;
                //设置启动参数  
                //startInfo.Arguments = String.Join(" ", Args);
                //设置启动动作,确保以管理员身份运行  
                startInfo.Verb = "runas";
                //如果不是管理员，则启动UAC  
                System.Diagnostics.Process.Start(startInfo);
                //退出  
                return;
            }

            try { standalone = bool.Parse(ConfigurationManager.AppSettings["Standalone"]); }
            catch { }

            ushort hbPort = 0;//心跳端口
            if (!standalone)
            {
                //Mode A. With service.
                if (args.Length < 1)
                {
                    return;
                }
                try { hbPort = ushort.Parse(args[0]); }
                catch { }
                if (hbPort == 0)
                {
                    return;
                }
            }
            else
            {
                //Mode B. Standalone.
            }

            Base dc = new Base();
            dc.OnMsg += dcOnMsg;
            dc.Init(hbPort);

            dc.Start();

            string[] sep = new string[] { " " };
            while (true)
            {
                if (!standalone)
                {
                    System.Threading.Thread.Sleep(1000);
                    continue;
                }

                try
                {
                    string cmd = Console.ReadLine();
                    if (!string.IsNullOrEmpty(cmd))
                    {
                        string[] ss = cmd.Trim().Split(sep, StringSplitOptions.RemoveEmptyEntries);
                        if (ss.Length == 0 || string.IsNullOrEmpty(ss[0]))
                        {
                            continue;
                        }
                        if (ss[0].ToUpper() == "EXIT")
                        {
                            ShowMsg("About to quit.");
                            dc.Stop();
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShowMsg(ex.Message);
                }
            }
        }
    }
}
