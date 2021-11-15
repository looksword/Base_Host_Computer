using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lib
{
    public class Lib
    {
    }

    public delegate void HandleStringMsg(StringMsgType msgType, string msg);

    public enum StringMsgType
    {
        Data = 0,
        Error,
        Info,
        Mark,
        Warning
    }

    public class PLCData
    {
        public string Value = "";
        public bool ValueChanged = false;
    }
}
